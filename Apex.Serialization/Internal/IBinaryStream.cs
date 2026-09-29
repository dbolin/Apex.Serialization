using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Apex.Serialization.Internal
{
    internal unsafe interface IBinaryStream : IDisposable
    {
        void ReadFrom(Stream stream);
        void WriteTo(Stream stream);

        void ReserveSize(int sizeNeeded);
        bool Flush();
        void Write(string? input);
        void WriteTypeId(Type type);
        void Write<T>(T value) where T : struct;
        void WriteValues<T>(void* source, uint byteLength) where T : struct;

        string? Read();
        byte* ReadTypeId(out int length1, out int length2);
        Type RestoreTypeFromId(ref byte* typeId, int typeLen1, int typeLen2);
        T Read<T>() where T : struct;
        void ReadBytes(void* destination, uint length);
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

        internal static readonly MethodInfo ReadBytesMethodInfo =
            typeof(TStream).GetMethod("ReadBytes", new[] { typeof(void*), typeof(uint) })!;

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