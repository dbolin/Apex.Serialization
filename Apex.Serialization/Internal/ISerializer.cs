using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Apex.Serialization.Extensions;

namespace Apex.Serialization.Internal
{
    internal interface ISerializer
    {
        void Write<T>(T value, Stream outputStream);
        T Read<T>(Stream outputStream);

        List<object> LoadedObjectRefs { get; }
        List<Type> LoadedTypeRefs { get; }

        bool WriteObjectRef(object value);
        void WriteTypeRef(Type value);

        Type ReadTypeRef();

        bool WriteNullByte(object value);
        bool WriteNullableByte<T>(T? value) where T : struct;
        bool ReadNullByte();

        void WriteFunction(Delegate value);
        Delegate ReadFunction();

        void WriteValuesArray(object array, int length, int elementSize);
        void ReadIntoValuesArray(object array, int elementSize);

        void QueueAfterDeserializationHook(Action<object> method, object instance);

        IBinaryWriter BinaryWriter { get; }
        IBinaryReader BinaryReader { get; }
    }

    internal static class SerializerMethods
    {
        internal static readonly MethodInfo SavedReferencesGetter =
            typeof(ISerializer).GetProperty("LoadedObjectRefs").GetMethod;

        internal static readonly MethodInfo WriteObjectRefMethod =
            typeof(ISerializer).GetMethod("WriteObjectRef");

        internal static readonly MethodInfo WriteTypeRefMethod =
            typeof(ISerializer).GetMethod("WriteTypeRef");

        internal static readonly MethodInfo ReadTypeRefMethod =
            typeof(ISerializer).GetMethod("ReadTypeRef");

        internal static readonly MethodInfo SavedReferencesListAdd =
            typeof(List<object>).GetMethod("Add");

        internal static readonly PropertyInfo SavedReferencesListIndexer =
            typeof(List<object>).GetProperty("Item", new[] {typeof(int)});

        internal static readonly MethodInfo LoadedTypeReferencesGetter =
            typeof(ISerializer).GetProperty("LoadedTypeRefs").GetMethod;

        internal static readonly PropertyInfo LoadedTypeListIndexer =
            typeof(List<Type>).GetProperty("Item", new[] { typeof(int) });

        internal static readonly MethodInfo BinaryWriterGetter =
            typeof(ISerializer).GetProperty("BinaryWriter").GetMethod;

        internal static readonly MethodInfo BinaryReaderGetter =
            typeof(ISerializer).GetProperty("BinaryReader").GetMethod;

        internal static readonly MethodInfo WriteNullByteMethod = typeof(ISerializer).GetMethod("WriteNullByte");
        internal static readonly MethodInfo WriteNullableByteMethod = typeof(ISerializer).GetMethod("WriteNullableByte");
        internal static readonly MethodInfo ReadNullByteMethod = typeof(ISerializer).GetMethod("ReadNullByte");

        internal static readonly MethodInfo WriteFunctionMethod = typeof(ISerializer).GetMethod("WriteFunction");
        internal static readonly MethodInfo ReadFunctionMethod = typeof(ISerializer).GetMethod("ReadFunction");

        internal static readonly MethodInfo WriteArrayOfValuesMethod = typeof(ISerializer).GetMethod("WriteValuesArray");
        internal static readonly MethodInfo ReadArrayOfValuesMethod = typeof(ISerializer).GetMethod("ReadIntoValuesArray");

        internal static readonly MethodInfo QueueAfterDeserializationHook =
            typeof(ISerializer).GetMethod("QueueAfterDeserializationHook");
    }
}