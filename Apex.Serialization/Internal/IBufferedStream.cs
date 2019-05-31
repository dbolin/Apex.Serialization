using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Apex.Serialization.Internal
{
    internal unsafe interface IBufferedStream : IDisposable
    {
        void ReadFrom(Stream stream);
        void WriteTo(Stream stream);

        void ReserveSize(int sizeNeeded);
        bool Flush();
        void Write(string input);
        void WriteTypeId(Type type);
        void Write<T>(T value) where T : struct;
        void WriteBytes(void* source, uint length);

        string Read();
        byte* ReadTypeId(out int length1, out int length2);
        Type RestoreTypeFromId(ref byte* typeId, int typeLen1, int typeLen2);
        T Read<T>() where T : struct;
        void ReadBytes(void* destination, uint length);
    }

    internal static class BufferedStreamMethods<TStream> where TStream : IBufferedStream
    {
        internal static readonly MethodInfo ReserveSizeMethodInfo = typeof(TStream).GetMethod("ReserveSize", new[] { typeof(int) });

        internal static readonly MethodInfo WriteStringMethodInfo =
            typeof(TStream).GetMethod("Write", new[] { typeof(string) });

        internal static readonly MethodInfo ReadStringMethodInfo =
            typeof(TStream).GetMethods().Single(x => x.Name == "Read" && !x.IsGenericMethod);

        internal static readonly MethodInfo WriteTypeIdMethodInfo =
            typeof(TStream).GetMethod("WriteTypeId", new[] { typeof(Type) });

        internal static readonly MethodInfo WriteBytesMethodInfo =
            typeof(TStream).GetMethod("WriteBytes", new[] { typeof(void*), typeof(uint) });

        internal static readonly MethodInfo ReadBytesMethodInfo =
            typeof(TStream).GetMethod("ReadBytes", new[] { typeof(void*), typeof(uint) });

        internal static Dictionary<Type, MethodInfo> primitiveWriteMethods = new Dictionary<Type, MethodInfo>
        {
            {typeof(bool), GenericMethods<bool>.WriteValueMethodInfo},
            {typeof(byte), GenericMethods<byte>.WriteValueMethodInfo},
            {typeof(sbyte), GenericMethods<sbyte>.WriteValueMethodInfo},
            {typeof(char), GenericMethods<char>.WriteValueMethodInfo},
            {typeof(decimal), GenericMethods<decimal>.WriteValueMethodInfo},
            {typeof(double), GenericMethods<double>.WriteValueMethodInfo},
            {typeof(float), GenericMethods<float>.WriteValueMethodInfo},
            {typeof(int), GenericMethods<int>.WriteValueMethodInfo},
            {typeof(uint), GenericMethods<uint>.WriteValueMethodInfo},
            {typeof(long), GenericMethods<long>.WriteValueMethodInfo},
            {typeof(ulong), GenericMethods<ulong>.WriteValueMethodInfo},
            {typeof(short), GenericMethods<short>.WriteValueMethodInfo},
            {typeof(ushort), GenericMethods<ushort>.WriteValueMethodInfo},
            {typeof(Guid), GenericMethods<Guid>.WriteValueMethodInfo},
        };

        internal static Dictionary<Type, MethodInfo> primitiveReadMethods = new Dictionary<Type, MethodInfo>
        {
            {typeof(bool), GenericMethods<bool>.ReadValueMethodInfo},
            {typeof(byte), GenericMethods<byte>.ReadValueMethodInfo},
            {typeof(sbyte), GenericMethods<sbyte>.ReadValueMethodInfo},
            {typeof(char), GenericMethods<char>.ReadValueMethodInfo},
            {typeof(decimal), GenericMethods<decimal>.ReadValueMethodInfo},
            {typeof(double), GenericMethods<double>.ReadValueMethodInfo},
            {typeof(float), GenericMethods<float>.ReadValueMethodInfo},
            {typeof(int), GenericMethods<int>.ReadValueMethodInfo},
            {typeof(uint), GenericMethods<uint>.ReadValueMethodInfo},
            {typeof(long), GenericMethods<long>.ReadValueMethodInfo},
            {typeof(ulong), GenericMethods<ulong>.ReadValueMethodInfo},
            {typeof(short), GenericMethods<short>.ReadValueMethodInfo},
            {typeof(ushort), GenericMethods<ushort>.ReadValueMethodInfo},
            {typeof(Guid), GenericMethods<Guid>.ReadValueMethodInfo},
        };

        internal static class GenericMethods<T> where T : struct
        {
            internal static readonly MethodInfo WriteValueMethodInfo = typeof(TStream).GetMethods().Single(x => x.Name == "Write" && x.IsGenericMethod).MakeGenericMethod(typeof(T));
            internal static readonly MethodInfo ReadValueMethodInfo = typeof(TStream).GetMethods().Single(x => x.Name == "Read" && x.IsGenericMethod).MakeGenericMethod(typeof(T));
        }
    }
}