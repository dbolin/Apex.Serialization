using System;
using System.Collections.Generic;
using System.Linq;
using FastExpressionCompiler.LightExpression;
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

            getInvocationCount = Expression.Lambda<Func<MulticastDelegate, IntPtr>>(Expression.Field(delegateParam, "_invocationCount"), delegateParam).CompileFast();
            getInvocationList = Expression.Lambda<Func<MulticastDelegate, object>>(Expression.Field(delegateParam, "_invocationList"), delegateParam).CompileFast();
            setInvocationCount = Expression.Lambda<Action<MulticastDelegate, IntPtr>>(Expression.Assign(Expression.Field(delegateParam, "_invocationCount"), intPtrParam), delegateParam, intPtrParam).CompileFast();
            setInvocationList = Expression.Lambda<Action<MulticastDelegate, object>>(Expression.Assign(Expression.Field(delegateParam, "_invocationList"), objectParam), delegateParam, objectParam).CompileFast();
            getInvocationListLogicallyNull = Expression.Lambda<Func<MulticastDelegate, bool>>(Expression.Call(delegateParam, logicallyNullMethod), delegateParam).CompileFast();
        }

        private static DictionarySlim<Type, List<FieldInfo>> _cache = new DictionarySlim<Type, List<FieldInfo>>();
        private static DictionarySlim<Type, List<FieldInfo>> _flattenedCache = new DictionarySlim<Type, List<FieldInfo>>();
        private static DictionarySlim<OrderedTypeCacheKey, List<FieldInfo>> _orderedCache = new DictionarySlim<OrderedTypeCacheKey, List<FieldInfo>>();

        private struct OrderedTypeCacheKey
        {
            public Type Type;
            public bool Flattened;
        }

        private static object _cacheLock = new object();

        internal static bool IsPrimitive(Type x)
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
        
        internal static (int size, bool isRef) GetSizeForType(Type type)
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
            var fields = GetFields(type);
            int size;

            if (type.IsGenericType && typeof(Nullable<>) == type.GetGenericTypeDefinition())
            {
                var (innerSize, isRef) = GetSizeForType(type.GenericTypeArguments[0]);
                if(isRef)
                {
                    sizeForField = 5;
                    return false;
                }

                sizeForField = innerSize + Unsafe.SizeOf<byte>();
                return true;
            }

            if (type.IsValueType && fields.All(f => IsPrimitive(f.FieldType)))
            {
                if(fields.Count == 0)
                {
                    size = 1;
                }
                else
                {
                    size = (int)UnsafeSizeOfMethodInfo.MakeGenericMethod(type)!
                        .Invoke(null, Array.Empty<Type>())!;
                }

                primitiveTypeSizeDictionary.Add(type, size);

                sizeForField = size;
                return true;
            }

            sizeForField = 5;
            return false;
        }

        internal static List<FieldInfo> GetOrderedFields(Type type, ImmutableSettings settings)
        {
            var mustUseReflectionToSetReadonly = FieldInfoModifier.MustUseReflectionToSetReadonly(settings);
            lock (_cacheLock)
            {
                ref var fields = ref _orderedCache.GetOrAddValueRef(new OrderedTypeCacheKey { Type = type, Flattened = settings.FlattenClassHierarchy });
                if (fields == null)
                {
                    var unorderedFields = settings.FlattenClassHierarchy ? GetFlattenedFields(type) : GetFields(type);
                    if (mustUseReflectionToSetReadonly)
                    {
                        fields = unorderedFields.OrderBy(x => IsPrimitive(x.FieldType) ? 0 : 1)
                            .ThenBy(x => x.IsInitOnly ? 0 : 1)
                            .ThenBy(x => x.FieldType == typeof(string) ? 0 : 1)
                            .ThenBy(x => x.Name).ToList();
                    }
                    else
                    {
                        fields = unorderedFields.OrderBy(x => IsPrimitive(x.FieldType) ? 0 : 1)
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

        private static List<FieldInfo> GetFields(Type type)
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
