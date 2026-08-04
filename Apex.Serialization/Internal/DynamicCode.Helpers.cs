using Apex.Serialization.Internal.Reflection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Apex.Serialization.Internal
{
    internal static partial class DynamicCode<TStream, TBinary>
       where TStream : IBinaryStream
       where TBinary : ISerializer
    {
        internal static MethodInfo GetUnitializedObjectMethodInfo = typeof(RuntimeHelpers).GetMethod("GetUninitializedObject")!;
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

        /// <summary>
        /// The empty field list used for boundary types.  Allocated per call rather than shared: it is
        /// passed into the same parameter that mutable field caches fill elsewhere, so a single shared
        /// instance would let any future mutation corrupt every boundary type's generated code
        /// process-wide.  This runs once per code generation, so the allocation is irrelevant.
        /// </summary>
        private static List<FieldInfo> NoFields() => new List<FieldInfo>();

        /// <summary>
        /// Returns the marked boundary type <paramref name="type"/> resolves to, or null if it is not a
        /// boundary.  Throws if a boundary type is reached while generating Tree mode code, where there
        /// is no reference table to resolve repeated occurrences through.
        /// </summary>
        private static Type? CheckBoundaryType(Type type, ImmutableSettings settings)
        {
            var boundaryType = settings.IsBoundaryType(type);
            if (boundaryType != null && settings.SerializationMode != Mode.Graph)
            {
                throw new NotSupportedException($"Type {type.FullName} is marked as a serialization boundary, which is only supported for Graph serialization");
            }

            return boundaryType;
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

        private static ConcurrentDictionary<Type, bool> _isBlittableCache = new ConcurrentDictionary<Type, bool>();

        internal static bool IsBlittable(Type elementType)
        {
            return _isBlittableCache.GetOrAdd(elementType, _ =>
            {
                if (!elementType.IsValueType)
                {
                    return false;
                }

                if(!TypeFields.IsPrimitive(elementType))
                {
                    return false;
                }

                try
                {
                    WriteArrayOfValuesMethod1.MakeGenericMethod(elementType);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
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

        private static readonly MethodInfo BinaryWriterGetter =
            typeof(TBinary).GetProperty("BinaryWriter", InstanceFlags)!.GetMethod!;

        private static readonly MethodInfo BinaryReaderGetter =
            typeof(TBinary).GetProperty("BinaryReader", InstanceFlags)!.GetMethod!;

        private static readonly MethodInfo CustomContextGetter =
            typeof(TBinary).GetMethod("GetCustomContext", InstanceFlags)!;

        private static readonly MethodInfo DisallowReadingObjectReference =
            typeof(TBinary).GetMethod("DisallowReadingObjectReference", InstanceFlags)!;

        private static readonly MethodInfo AllowReadingObjectReference =
            typeof(TBinary).GetMethod("AllowReadingObjectReference", InstanceFlags)!;

        private static readonly MethodInfo CheckReadingObjectReference =
            typeof(TBinary).GetMethod("CheckReadingObjectReference", InstanceFlags)!;

        private static readonly MethodInfo CheckSerializedVersionUniqueIdMethod =
            typeof(TBinary).GetMethod("CheckSerializedVersionUniqueId", InstanceFlags, new Type[] { })!;
        private static readonly MethodInfo WriteSerializedVersionUniqueIdMethod =
            typeof(TBinary).GetMethod("WriteSerializedVersionUniqueId", InstanceFlags, new Type[] { })!;

        private static readonly MethodInfo WriteFunctionMethod = typeof(TBinary).GetMethod("WriteFunction", InstanceFlags)!;
        private static readonly MethodInfo ReadFunctionMethod = typeof(TBinary).GetMethod("ReadFunction", InstanceFlags)!;

        private static readonly MethodInfo WriteArrayOfValuesMethod1 = typeof(TBinary).GetMethod("WriteValuesArray1", InstanceFlags)!;
        private static readonly MethodInfo ReadArrayOfValuesMethod1 = typeof(TBinary).GetMethod("ReadValuesArray1", InstanceFlags)!;
        private static readonly MethodInfo WriteArrayOfValuesMethod2 = typeof(TBinary).GetMethod("WriteValuesArray2", InstanceFlags)!;
        private static readonly MethodInfo ReadArrayOfValuesMethod2 = typeof(TBinary).GetMethod("ReadValuesArray2", InstanceFlags)!;

        private static readonly MethodInfo QueueAfterDeserializationHook =
            typeof(TBinary).GetMethod("QueueAfterDeserializationHook", InstanceFlags)!;

        private static readonly MethodInfo GetBoundarySubstituteMethod =
            typeof(TBinary).GetMethod("GetBoundarySubstitute", InstanceFlags)!;
    }
}
