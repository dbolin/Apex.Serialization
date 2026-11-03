using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Apex.Serialization.Internal
{
    /// <summary>
    /// The byte ranges of a value type's memory that no field covers.  A value type whose fields are all primitive
    /// is written as one block of its in-memory size, so its padding reaches the output holding whatever the source
    /// memory held.  The writers zero these ranges in the output buffer itself, since a by-value copy can carry
    /// different padding than the value it was copied from, so equal values serialize to equal bytes; field bytes
    /// are copied unchanged.
    ///
    /// Field offsets come from <c>ldflda</c> in generated IL, because <see cref="FieldInfo"/> exposes no offsets for
    /// sequential or auto layout and <c>Marshal.OffsetOf</c> reports the marshaled layout rather than the managed one.
    /// A struct with a declared size counts as fully covered, since the bytes past its fields may hold data (the
    /// compiler declares a size for each fixed buffer, and a hand-sized inline buffer is addressed past its first
    /// field).  The tradeoff is that real padding stays in a struct whose size is declared to match a native layout
    /// or a cache line.  A fieldless struct is the exception when its declared size is the 1 byte the compiler
    /// declares for every empty struct: that byte is padding.
    ///
    /// A value type marked <c>[Intrinsic]</c> also counts as fully covered, because the runtime defines its layout
    /// and can size it past its declared fields: <c>Vector&lt;T&gt;</c> declares 16 bytes of fields but is as wide
    /// as the hardware vector, so a map built from its fields would zero its upper lanes.
    /// </summary>
    internal static class PaddingMap
    {
        private static readonly ConcurrentDictionary<Type, bool[]> _coverage = new ConcurrentDictionary<Type, bool[]>();
        private static readonly ConcurrentDictionary<Type, (int Start, int Length)[]> _unfieldedDataRanges = new ConcurrentDictionary<Type, (int Start, int Length)[]>();

        internal static (int Start, int Length)[] GetRanges(Type type)
        {
            return Uncovered(Coverage(type));
        }

        /// <summary>
        /// Empty for a sequential struct holding a reference, whose declared size the runtime ignores even when its own
        /// layout happens to come out at that size, and for any struct the runtime gave a size other than the declared one.
        /// </summary>
        internal static (int Start, int Length)[] GetUnfieldedDataRanges(Type type) => _unfieldedDataRanges.GetOrAdd(type, ComputeUnfieldedDataRanges);

        private static (int Start, int Length)[] ComputeUnfieldedDataRanges(Type type)
        {
            var size = RuntimeHelpers.SizeOf(type.TypeHandle);
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (!HasOpaqueDeclaredSize(type, fields) || size != type.StructLayoutAttribute?.Size
                || (type.IsLayoutSequential && HoldsReferences(type)))
            {
                return Array.Empty<(int, int)>();
            }

            var covered = new bool[size];
            var offsets = FieldOffsets(type, fields);
            for (var i = 0; i < fields.Length; ++i)
            {
                var fieldType = fields[i].FieldType;
                covered.AsSpan(offsets[i], fieldType.IsValueType ? RuntimeHelpers.SizeOf(fieldType.TypeHandle) : IntPtr.Size).Fill(true);
            }

            return Uncovered(covered);
        }

        private static readonly MethodInfo IsReferenceOrContainsReferencesMethod =
            typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!;

        private static bool HoldsReferences(Type type)
        {
            return (bool)IsReferenceOrContainsReferencesMethod.MakeGenericMethod(type).Invoke(null, null)!;
        }

        private static (int Start, int Length)[] Uncovered(bool[] covered)
        {
            var ranges = new List<(int Start, int Length)>();
            for (var start = 0; start < covered.Length; ++start)
            {
                if (covered[start])
                {
                    continue;
                }

                var end = start;
                while (end < covered.Length && !covered[end])
                {
                    ++end;
                }

                ranges.Add((start, end - start));
                start = end;
            }

            return ranges.ToArray();
        }

        private static bool[] Coverage(Type type) => _coverage.GetOrAdd(type, ComputeCoverage);

        private static bool[] ComputeCoverage(Type type)
        {
            if (!type.IsValueType)
            {
                return FullyCovered(IntPtr.Size);
            }

            var size = RuntimeHelpers.SizeOf(type.TypeHandle);
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (type.IsPrimitive || type.IsEnum || IsIntrinsic(type) || HasOpaqueDeclaredSize(type, fields))
            {
                return FullyCovered(size);
            }

            var covered = new bool[size];
            if (type.IsDefined(typeof(InlineArrayAttribute), false))
            {
                var element = Coverage(fields[0].FieldType);
                for (var offset = 0; offset < size; offset += element.Length)
                {
                    element.CopyTo(covered, offset);
                }

                return covered;
            }

            var offsets = FieldOffsets(type, fields);
            for (var i = 0; i < fields.Length; ++i)
            {
                var field = Coverage(fields[i].FieldType);
                for (var b = 0; b < field.Length; ++b)
                {
                    covered[offsets[i] + b] |= field[b];
                }
            }

            return covered;
        }

        private static bool IsIntrinsic(Type type)
        {
            return type.GetCustomAttributesData().Any(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.IntrinsicAttribute");
        }

        private static bool HasOpaqueDeclaredSize(Type type, FieldInfo[] fields)
        {
            var declaredSize = type.StructLayoutAttribute?.Size ?? 0;
            return fields.Length > 0 ? declaredSize > 0 : declaredSize > 1;
        }

        private static bool[] FullyCovered(int size)
        {
            var covered = new bool[size];
            Array.Fill(covered, true);
            return covered;
        }

        private static int[] FieldOffsets(Type type, FieldInfo[] fields)
        {
            var method = new DynamicMethod($"Apex.Serialization.FieldOffsets_{type.FullName}", typeof(void), new[] { typeof(int[]) }, restrictedSkipVisibility: true);
            var il = method.GetILGenerator();
            var instance = il.DeclareLocal(type);
            for (var i = 0; i < fields.Length; ++i)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloca, instance);
                il.Emit(OpCodes.Ldflda, fields[i]);
                il.Emit(OpCodes.Ldloca, instance);
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Stelem_I4);
            }

            il.Emit(OpCodes.Ret);

            var offsets = new int[fields.Length];
            method.CreateDelegate<Action<int[]>>()(offsets);
            return offsets;
        }
    }

    internal static class PaddingMap<T> where T : struct
    {
        private const int MinimumTileLength = 1024;

        private static readonly (int Start, int Length)[] Ranges = PaddingMap.GetRanges(typeof(T));

        internal static readonly bool HasPadding = PaddingMap.GetRanges(typeof(T)).Length > 0;

        private static readonly byte[] KeepMask = BuildKeepMask(PaddingMap.GetRanges(typeof(T)));

        // Stores zeros rather than applying KeepMask: loading a word that the value's narrower field stores have
        // just written stalls on store-to-load forwarding.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ZeroPadding(Span<byte> value)
        {
            ref var first = ref MemoryMarshal.GetReference(value.Slice(0, Unsafe.SizeOf<T>()));
            foreach (var (start, length) in Ranges)
            {
                var at = start;
                var end = start + length;
                for (; at + sizeof(ulong) <= end; at += sizeof(ulong))
                {
                    Unsafe.WriteUnaligned<ulong>(ref Unsafe.Add(ref first, at), 0);
                }

                if (at + sizeof(uint) <= end)
                {
                    Unsafe.WriteUnaligned<uint>(ref Unsafe.Add(ref first, at), 0);
                    at += sizeof(uint);
                }

                if (at + sizeof(ushort) <= end)
                {
                    Unsafe.WriteUnaligned<ushort>(ref Unsafe.Add(ref first, at), 0);
                    at += sizeof(ushort);
                }

                if (at < end)
                {
                    Unsafe.Add(ref first, at) = 0;
                }
            }
        }

        /// <summary>
        /// Copies <paramref name="source"/>, a run of consecutive <typeparamref name="T"/> values that starts
        /// <paramref name="offset"/> bytes into them, to <paramref name="destination"/>, zeroing the padding in the copy.
        /// </summary>
        internal static void CopyZeroingPadding(Span<byte> destination, ReadOnlySpan<byte> source, uint offset)
        {
            var length = (uint)source.Length;
            ref var to = ref MemoryMarshal.GetReference(destination.Slice(0, source.Length));
            ref var from = ref MemoryMarshal.GetReference(source);
            var tileLength = (uint)(KeepMask.Length - Unsafe.SizeOf<T>());
            ref var inPhase = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(KeepMask), offset % (uint)Unsafe.SizeOf<T>());
            for (uint done = 0; done < length; done += tileLength)
            {
                KeepMasked(ref Unsafe.Add(ref to, done), ref Unsafe.Add(ref from, done), ref inPhase, Math.Min(tileLength, length - done));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void KeepMasked(ref byte destination, ref byte source, ref byte mask, uint length)
        {
            uint i = 0;
            for (; i + (uint)Vector<byte>.Count <= length; i += (uint)Vector<byte>.Count)
            {
                Vector.StoreUnsafe(Vector.LoadUnsafe(ref source, i) & Vector.LoadUnsafe(ref mask, i), ref destination, i);
            }

            for (; i + sizeof(ulong) <= length; i += sizeof(ulong))
            {
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, i), Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref source, i)) & Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref mask, i)));
            }

            for (; i < length; ++i)
            {
                Unsafe.Add(ref destination, i) = (byte)(Unsafe.Add(ref source, i) & Unsafe.Add(ref mask, i));
            }
        }

        private static byte[] BuildKeepMask((int Start, int Length)[] ranges)
        {
            if (ranges.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var size = Unsafe.SizeOf<T>();
            var tileLength = size * ((MinimumTileLength + size - 1) / size);
            var keepMask = new byte[tileLength + size];
            Array.Fill(keepMask, byte.MaxValue);
            for (var value = 0; value < keepMask.Length; value += size)
            {
                foreach (var (start, length) in ranges)
                {
                    keepMask.AsSpan(value + start, length).Clear();
                }
            }

            return keepMask;
        }
    }
}
