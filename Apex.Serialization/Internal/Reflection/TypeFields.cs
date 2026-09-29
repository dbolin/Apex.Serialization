using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Apex.Serialization.Internal.Reflection
{
    internal static class TypeFields
    {
        internal static Func<MulticastDelegate, bool> getInvocationListLogicallyNull;
        internal static Func<MulticastDelegate, IntPtr> getInvocationCount;
        internal static Action<MulticastDelegate, IntPtr> setInvocationCount;
        internal static Func<MulticastDelegate, object> getInvocationList;
        internal static Action<MulticastDelegate, object> setInvocationList;

        static TypeFields()
        {
            var delegateParam = Expression.Parameter(typeof(MulticastDelegate));
            var intPtrParam = Expression.Parameter(typeof(IntPtr));
            var objectParam = Expression.Parameter(typeof(object));

            var logicallyNullMethod = typeof(MulticastDelegate).GetMethod("InvocationListLogicallyNull", BindingFlags.Instance | BindingFlags.NonPublic)!;

            getInvocationCount = Expression.Lambda<Func<MulticastDelegate, IntPtr>>(Expression.Field(delegateParam, "_invocationCount"), delegateParam).Compile();
            getInvocationList = Expression.Lambda<Func<MulticastDelegate, object>>(Expression.Field(delegateParam, "_invocationList"), delegateParam).Compile();
            setInvocationCount = Expression.Lambda<Action<MulticastDelegate, IntPtr>>(Expression.Assign(Expression.Field(delegateParam, "_invocationCount"), intPtrParam), delegateParam, intPtrParam).Compile();
            setInvocationList = Expression.Lambda<Action<MulticastDelegate, object>>(Expression.Assign(Expression.Field(delegateParam, "_invocationList"), objectParam), delegateParam, objectParam).Compile();
            getInvocationListLogicallyNull = Expression.Lambda<Func<MulticastDelegate, bool>>(Expression.Call(delegateParam, logicallyNullMethod), delegateParam).Compile();
        }

        private static DictionarySlim<Type, List<FieldInfo>> _cache = new DictionarySlim<Type, List<FieldInfo>>();
        private static DictionarySlim<Type, List<FieldInfo>> _flattenedCache = new DictionarySlim<Type, List<FieldInfo>>();
        private static DictionarySlim<OrderedTypeCacheKey, List<FieldInfo>> _orderedCache = new DictionarySlim<OrderedTypeCacheKey, List<FieldInfo>>();

        private struct OrderedTypeCacheKey
        {
            public Type Type;
            public bool Flattened;
            public bool ReadonlyFieldsFirst;
            public ImmutableSettings? CustomValueSettings;
        }

        private static object _cacheLock = new object();

        internal static bool IsNeverCustomSerialized(Type type)
        {
            return type.IsEnum || BuiltInPrimitives.Contains(type);
        }

        /// <summary>
        /// A block copy, reserved size or field order decided by <see cref="HasPrimitiveShape(Type)"/> instead copies the
        /// memory of a value whose registered serializer should run.
        /// </summary>
        internal static bool IsPrimitive(Type type, ImmutableSettings settings)
        {
            return HasPrimitiveShape(type) && !settings.ContainsCustomSerializedValue(type);
        }

        internal static (int size, bool isRef) GetSizeForType(Type type, ImmutableSettings settings)
        {
            return settings.ContainsCustomSerializedValue(type) ? (0, true) : GetShapeSize(type);
        }

        internal static bool HasPrimitiveShape(Type x)
        {
            lock (_cacheLock)
            {
                if (primitiveTypeSizeDictionary.ContainsKey(x))
                {
                    return true;
                }

                if (TryGetSizeForStruct(x, out _))
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Unlike <see cref="HasPrimitiveShape(Type)"/> it ignores a skipped [NonSerialized] field, which changes how a value
        /// is written but not what reading it can create.
        /// </summary>
        internal static bool HoldsOnlyPrimitives(Type type)
        {
            if (HasPrimitiveShape(type))
            {
                return true;
            }

            return type.IsValueType && !type.ContainsGenericParameters && GetFields(type).All(x => HoldsOnlyPrimitives(x.FieldType));
        }

        private static Dictionary<Type, int> primitiveTypeSizeDictionary = new Dictionary<Type, int>
        {
            {typeof(bool), Unsafe.SizeOf<bool>()},
            {typeof(byte), Unsafe.SizeOf<byte>()},
            {typeof(sbyte), Unsafe.SizeOf<sbyte>()},
            {typeof(char), Unsafe.SizeOf<char>()},
            {typeof(decimal), Unsafe.SizeOf<decimal>()},
            {typeof(double), Unsafe.SizeOf<double>()},
            {typeof(float), Unsafe.SizeOf<float>()},
            {typeof(int), Unsafe.SizeOf<int>()},
            {typeof(uint), Unsafe.SizeOf<uint>()},
            {typeof(long), Unsafe.SizeOf<long>()},
            {typeof(ulong), Unsafe.SizeOf<ulong>()},
            {typeof(short), Unsafe.SizeOf<short>()},
            {typeof(ushort), Unsafe.SizeOf<ushort>()},
            {typeof(IntPtr), IntPtr.Size},
            {typeof(UIntPtr), UIntPtr.Size},
            {typeof(Guid), Unsafe.SizeOf<Guid>()},
        };

        private static readonly HashSet<Type> BuiltInPrimitives = new HashSet<Type>(primitiveTypeSizeDictionary.Keys);

        internal static (int size, bool isRef) GetShapeSize(Type type)
        {
            lock (_cacheLock)
            {
                if (primitiveTypeSizeDictionary.TryGetValue(type, out var size))
                {
                    return (size, false);
                }

                if (TryGetSizeForStruct(type, out var sizeForField))
                {
                    return (sizeForField, false);
                }
            }

            return (0, true);
        }

        private static readonly MethodInfo UnsafeSizeOfMethodInfo = typeof(Unsafe).GetMethod("SizeOf")!;

        private static bool TryGetSizeForStruct(Type type, out int sizeForField)
        {
            if (type.ContainsGenericParameters)
            {
                sizeForField = 5;
                return false;
            }

            var fields = GetFields(type);

            if (type.IsGenericType && typeof(Nullable<>) == type.GetGenericTypeDefinition())
            {
                var (innerSize, isRef) = GetShapeSize(type.GenericTypeArguments[0]);
                if(isRef)
                {
                    sizeForField = 5;
                    return false;
                }

                sizeForField = innerSize + Unsafe.SizeOf<byte>();
                return true;
            }

            if (type.IsValueType && !SkipsFields(type, fields) && fields.All(f => HasPrimitiveShape(f.FieldType)))
            {
                var size = (int)UnsafeSizeOfMethodInfo.MakeGenericMethod(type)!
                    .Invoke(null, Array.Empty<Type>())!;

                primitiveTypeSizeDictionary.Add(type, size);

                sizeForField = size;
                return true;
            }

            sizeForField = 5;
            return false;
        }

        /// <summary>
        /// A block copy writes every field, so a struct that skips a [NonSerialized] one would carry its bytes (a pointer,
        /// for a reference).
        /// </summary>
        private static bool SkipsFields(Type type, List<FieldInfo> fields)
        {
            return fields.Count != type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length;
        }

        internal static List<FieldInfo> GetOrderedFields(Type type, ImmutableSettings settings)
        {
            var mustUseReflectionToSetReadonly = FieldInfoModifier.MustUseReflectionToSetReadonly(settings);
            lock (_cacheLock)
            {
                var key = new OrderedTypeCacheKey
                {
                    Type = type,
                    Flattened = settings.FlattenClassHierarchy,
                    ReadonlyFieldsFirst = mustUseReflectionToSetReadonly,
                    CustomValueSettings = settings.HasCustomValueTypeSerializers ? settings : null,
                };
                ref var fields = ref _orderedCache.GetOrAddValueRef(key);
                if (fields == null)
                {
                    var unorderedFields = settings.FlattenClassHierarchy ? GetFlattenedFields(type) : GetFields(type);
                    if (mustUseReflectionToSetReadonly)
                    {
                        fields = unorderedFields.OrderBy(x => IsPrimitive(x.FieldType, settings) ? 0 : 1)
                            .ThenBy(x => x.IsInitOnly ? 0 : 1)
                            .ThenBy(x => x.FieldType == typeof(string) ? 0 : 1)
                            .ThenBy(x => x.Name).ToList();
                    }
                    else
                    {
                        fields = unorderedFields.OrderBy(x => IsPrimitive(x.FieldType, settings) ? 0 : 1)
                            .ThenBy(x => x.FieldType == typeof(string) ? 0 : 1)
                            .ThenBy(x => x.Name).ToList();
                    }
                }

                return fields;
            }
        }

        private static List<FieldInfo> GetFlattenedFields(Type type)
        {
            lock (_cacheLock)
            {
                ref var fields = ref _flattenedCache.GetOrAddValueRef(type);
                var originalType = type;
                var start = Enumerable.Empty<FieldInfo>();
                while (type != null)
                {
                    if (type.Module.ScopeName == "CommonLanguageRuntimeLibrary")
                    {
                        start = start.Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                            BindingFlags.NonPublic |
                                                            BindingFlags.DeclaredOnly));
                    }
                    else
                    {
                        start = start.Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                            BindingFlags.NonPublic |
                                                            BindingFlags.DeclaredOnly)
                            .Where(x => x.CustomAttributes.All(a =>
                                a.AttributeType != typeof(NonSerializedAttribute))));
                    }

                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        start = start.Where(x => x.Name != "_keys" && x.Name != "_values");
                    }

                    type = type.BaseType!;
                }

                fields = start.ToList();
                return fields;
            }
        }

        internal static List<FieldInfo> GetFields(Type type)
        {
            lock (_cacheLock)
            {
                ref var fields = ref _cache.GetOrAddValueRef(type);
                var start = Enumerable.Empty<FieldInfo>();
                if (type.Module.ScopeName == "CommonLanguageRuntimeLibrary")
                {
                    start = start.Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                        BindingFlags.NonPublic |
                                                        BindingFlags.DeclaredOnly));
                }
                else
                {
                    start = start.Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                        BindingFlags.NonPublic |
                                                        BindingFlags.DeclaredOnly)
                        .Where(x => x.CustomAttributes.All(a =>
                            a.AttributeType != typeof(NonSerializedAttribute))));
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    start = start.Where(x => x.Name != "_keys" && x.Name != "_values");
                }

                fields = start.ToList();
                return fields;
            }
        }
    }
}
