using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Apex.Serialization.Internal
{
    internal static partial class DynamicCode<TStream, TBinary>
       where TStream : IBinaryStream
       where TBinary : ISerializer
    {
        internal static MethodInfo GetUnitializedObjectMethodInfo = typeof(FormatterServices).GetMethod("GetUninitializedObject")!;
        private static MethodInfo fieldInfoSetValueMethod = typeof(FieldInfo).GetMethod("SetValue", new[] { typeof(object), typeof(object) })!;

        private static Expression ReserveConstantSize(ParameterExpression stream, int size)
        {
            if (size <= 0)
            {
                return Expression.Empty();
            }

            return Expression.Call(stream, BinaryStreamMethods<TStream>.ReserveSizeMethodInfo,
                                Expression.Constant(size));
        }

        private static void CheckTypeSupported(Type type, List<FieldInfo> fields)
        {
            if (type.IsPointer || fields.Any(x => x.FieldType.IsPointer))
            {
                throw new NotSupportedException("Pointers or types containing pointers are not supported");
            }

            if (typeof(SafeHandle).IsAssignableFrom(type))
            {
                throw new NotSupportedException("Objects containing handles are not supported");
            }
        }

        private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MethodInfo SavedReferencesGetter =
            typeof(TBinary).GetProperty("LoadedObjectRefs", InstanceFlags)!.GetMethod!;

        private static readonly MethodInfo WriteObjectRefMethod =
            typeof(TBinary).GetMethod("WriteObjectRef", InstanceFlags)!;

        private static readonly MethodInfo WriteTypeRefMethod =
            typeof(TBinary).GetMethod("WriteTypeRef", InstanceFlags)!;

        private static readonly MethodInfo ReadTypeRefMethod =
            typeof(TBinary).GetMethod("ReadTypeRef", InstanceFlags)!;

        private static readonly MethodInfo SavedReferencesListAdd =
            typeof(List<object>).GetMethod("Add")!;

        private static readonly MethodInfo SavedReferencesListCountGetter =
            typeof(List<object>).GetProperty("Count")!.GetMethod!;

        private static readonly PropertyInfo SavedReferencesListIndexer =
            typeof(List<object>).GetProperty("Item", new[] { typeof(int) })!;

        private static readonly MethodInfo LoadedTypeReferencesGetter =
            typeof(TBinary).GetProperty("LoadedTypeRefs", InstanceFlags)!.GetMethod!;

        private static readonly PropertyInfo LoadedTypeListIndexer =
            typeof(List<Type>).GetProperty("Item", new[] { typeof(int) })!;

        private static readonly MethodInfo BinaryWriterGetter =
            typeof(TBinary).GetProperty("BinaryWriter", InstanceFlags)!.GetMethod!;

        private static readonly MethodInfo BinaryReaderGetter =
            typeof(TBinary).GetProperty("BinaryReader", InstanceFlags)!.GetMethod!;

        private static readonly MethodInfo CustomContextGetter =
            typeof(TBinary).GetMethod("GetCustomContext", InstanceFlags)!;

        private static readonly MethodInfo WriteFunctionMethod = typeof(TBinary).GetMethod("WriteFunction", InstanceFlags)!;
        private static readonly MethodInfo ReadFunctionMethod = typeof(TBinary).GetMethod("ReadFunction", InstanceFlags)!;

        private static readonly MethodInfo WriteArrayOfValuesMethod1 = typeof(TBinary).GetMethod("WriteValuesArray1", InstanceFlags)!;
        private static readonly MethodInfo ReadArrayOfValuesMethod1 = typeof(TBinary).GetMethod("ReadValuesArray1", InstanceFlags)!;
        private static readonly MethodInfo WriteArrayOfValuesMethod2 = typeof(TBinary).GetMethod("WriteValuesArray2", InstanceFlags)!;
        private static readonly MethodInfo ReadArrayOfValuesMethod2 = typeof(TBinary).GetMethod("ReadValuesArray2", InstanceFlags)!;

        private static readonly MethodInfo QueueAfterDeserializationHook =
            typeof(TBinary).GetMethod("QueueAfterDeserializationHook", InstanceFlags)!;
    }
}
