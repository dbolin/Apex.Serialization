using System;
using System.Collections.Concurrent;
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

            var logicallyNullMethod = typeof(MulticastDelegate).GetMethod("InvocationListLogicallyNull", BindingFlags.Instance | BindingFlags.NonPublic);

            getInvocationCount = Expression.Lambda<Func<MulticastDelegate, IntPtr>>(Expression.Field(delegateParam, "_invocationCount"), delegateParam).Compile();
            getInvocationList = Expression.Lambda<Func<MulticastDelegate, object>>(Expression.Field(delegateParam, "_invocationList"), delegateParam).Compile();
            setInvocationCount = Expression.Lambda<Action<MulticastDelegate, IntPtr>>(Expression.Assign(Expression.Field(delegateParam, "_invocationCount"), intPtrParam), delegateParam, intPtrParam).Compile();
            setInvocationList = Expression.Lambda<Action<MulticastDelegate, object>>(Expression.Assign(Expression.Field(delegateParam, "_invocationList"), objectParam), delegateParam, objectParam).Compile();
            getInvocationListLogicallyNull = Expression.Lambda<Func<MulticastDelegate, bool>>(Expression.Call(delegateParam, logicallyNullMethod), delegateParam).Compile();
        }

        private static DictionarySlim<Type, List<FieldInfo>> _cache = new DictionarySlim<Type, List<FieldInfo>>();
        private static DictionarySlim<Type, List<FieldInfo>> _orderedCache = new DictionarySlim<Type, List<FieldInfo>>();

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
        
        private static DictionarySlim<Type, int> structSizeDictionary = new DictionarySlim<Type, int>();

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

            return (5, true);
        }

        private static bool TryGetSizeForStruct(Type type, out int sizeForField)
        {
            ref int size = ref structSizeDictionary.GetOrAddValueRef(type);
            if (size != 0)
            {
                sizeForField = size;
                return true;
            }

            var fields = GetFields(type);

            if (type.IsValueType && fields.All(f => IsPrimitive(f.FieldType)))
            {
                size = (int) typeof(Unsafe).GetMethod("SizeOf")!.MakeGenericMethod(type)!
                    .Invoke(null, Array.Empty<Type>())!;

                sizeForField = size;
                return true;
            }

            sizeForField = 5;
            return false;
        }

        private static DictionarySlim<Type, Type> _collections = new DictionarySlim<Type, Type>();

        internal static Type? GetCustomCollectionBaseCollection(Type type)
        {
            if (_collections.TryGetValue(type, out var result))
            {
                return result;
            }

            return null;
        }

        internal static List<FieldInfo> GetOrderedFields(Type type)
        {
            lock (_cacheLock)
            {
                ref var fields = ref _orderedCache.GetOrAddValueRef(type);
                if (fields == null)
                {
                    var unorderedFields = GetFields(type);
                    if (FieldInfoModifier.MustUseReflectionToSetReadonly)
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

        private static List<FieldInfo> GetFields(Type type)
        {
            lock (_cacheLock)
            {
                ref var fields = ref _cache.GetOrAddValueRef(type);

                var originalType = type;
                var start = Enumerable.Empty<FieldInfo>();
                while (type != null)
                {
                    if (IsKnownCollection(type))
                    {
                        _collections.GetOrAddValueRef(originalType) = type;
                        //break;
                    }

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

                    type = type.BaseType!;
                }

                fields = start.ToList();
                return fields;
            }
        }

        private static HashSet<Type> _knownCollections = new HashSet<Type>
        {
            typeof(Dictionary<,>),
            typeof(SortedDictionary<,>),
            typeof(ConcurrentDictionary<,>),
            typeof(SortedList<,>),
            typeof(LinkedList<>),
            typeof(SortedSet<>),
            typeof(HashSet<>),
            typeof(ConcurrentQueue<>),
            typeof(ConcurrentBag<>),
        };

        internal static bool IsKnownCollection(Type type)
        {
            if (type.IsGenericType)
            {
                type = type.GetGenericTypeDefinition();
            }

            return _knownCollections.Contains(type);
        }
    }
}
