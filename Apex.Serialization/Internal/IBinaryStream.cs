using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Apex.Serialization.Internal
{
    internal interface IBinaryStream : IDisposable
    {
        void ReadFrom(Stream stream);
        void WriteTo(Stream stream);

        void ReserveSize(int sizeNeeded);
        bool Flush();
        void Write(string? input);
        void WriteTypeId(Type type);
        void Write<T>(T value) where T : struct;
        void WriteValues<T>(ReadOnlySpan<byte> source) where T : struct;

        string? Read();

        /// <summary>
        /// Returns the next type id with its length header, as <see cref="RestoreTypeFromId"/> takes it.  The span points
        /// into the stream's buffer, so it is valid only until the next call on the stream.
        /// </summary>
        ReadOnlySpan<byte> ReadTypeId();

        /// <param name="isCanonical">Whether <paramref name="typeId"/> is exactly what writing the returned type
        /// produces, so caching it can't grow the cache past one entry per type.</param>
        Type RestoreTypeFromId(ReadOnlySpan<byte> typeId, out bool isCanonical);

        T Read<T>() where T : struct;
        void ReadBytes(Span<byte> destination);

        /// <summary>
        /// Returns true when the stream holds <paramref name="byteCount"/> more bytes and throws
        /// <see cref="EndOfStreamException"/> when it can tell it doesn't, without growing its buffer: a stream that can
        /// seek is checked against its length, any other by buffering the bytes if they fit.  False means telling would
        /// take a larger buffer, so a caller sizing an allocation by a length from the stream has to grow it as the bytes
        /// arrive, or buffer them with <see cref="ReadAhead"/>.
        /// </summary>
        bool TryEnsureAvailable(long byteCount);

        /// <summary>
        /// Buffers <paramref name="byteCount"/> bytes, throwing <see cref="EndOfStreamException"/> if the stream ends
        /// first.  The buffer grows only once the stream has filled it, so it stays within twice what the stream
        /// supplied, until <see cref="ReleaseReadAhead"/>.
        /// </summary>
        void ReadAhead(int byteCount);

        void ThrowIfReadPastEnd();

        /// <summary>
        /// Throws <see cref="EndOfStreamException"/> if the stream ended before supplying the next
        /// <paramref name="byteCount"/> bytes of the current reservation, for a value that always takes all of them.
        /// </summary>
        void ThrowIfStreamEndsWithin(int byteCount);

        void ReleaseReadAhead();
    }

    internal static class BinaryStreamMethods<TStream> where TStream : IBinaryStream
    {
        internal static readonly MethodInfo ReserveSizeMethodInfo = typeof(TStream).GetMethod("ReserveSize", new[] { typeof(int) })!;

        internal static readonly MethodInfo WriteStringMethodInfo =
            typeof(TStream).GetMethod("Write", new[] { typeof(string) })!;

        internal static readonly MethodInfo ReadStringMethodInfo =
            typeof(TStream).GetMethods().Single(x => x.Name == "Read" && !x.IsGenericMethod);

        internal static readonly MethodInfo WriteTypeIdMethodInfo =
            typeof(TStream).GetMethod("WriteTypeId", new[] { typeof(Type) })!;

        internal static MethodInfo GetWriteValueMethodInfo(Type t)
        {
            // Initialized before the generated code that calls Write<t> is compiled, so the JIT folds
            // PaddingMap<t>.HasPadding to a constant and an unpadded type's write carries no padding check.
            RuntimeHelpers.RunClassConstructor(typeof(PaddingMap<>).MakeGenericType(t).TypeHandle);
            return typeof(TStream).GetMethods().Single(x => x.Name == "Write" && x.IsGenericMethod).MakeGenericMethod(t);
        }
        internal static MethodInfo GetReadValueMethodInfo(Type t) => typeof(TStream).GetMethods().Single(x => x.Name == "Read" && x.IsGenericMethod).MakeGenericMethod(t);

        internal static class GenericMethods<T> where T : struct
        {
            internal static readonly MethodInfo WriteValueMethodInfo = GetWriteValueMethodInfo(typeof(T));
            internal static readonly MethodInfo ReadValueMethodInfo = GetReadValueMethodInfo(typeof(T));
        }
    }
}
