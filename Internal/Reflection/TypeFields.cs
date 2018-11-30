﻿using System;
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

        private static object _cacheLock = new object();

        private static object _primitiveCacheLock = new object();

        internal static bool IsPrimitive(FieldInfo x)
        {
            lock (_primitiveCacheLock)
            {
                if (primitiveTypeSizeDictionary.ContainsKey(x.FieldType))
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
            {typeof(bool), 1},
            {typeof(byte), 1},
            {typeof(sbyte), 1},
            {typeof(char), 2},
            {typeof(decimal), 24},
            {typeof(double), 8},
            {typeof(float), 4},
            {typeof(int), 4},
            {typeof(uint), 4},
            {typeof(long), 8},
            {typeof(ulong), 8},
            {typeof(short), 2},
            {typeof(ushort), 2},
            {typeof(IntPtr), 8},
            {typeof(UIntPtr), 8},
            {typeof(Guid), 16},
        };
        
        private static Dictionary<Type, int> structSizeDictionary = new Dictionary<Type, int>();

        internal static int GetSizeForField(FieldInfo field)
        {
            lock (_primitiveCacheLock)
            {
                if (primitiveTypeSizeDictionary.TryGetValue(field.FieldType, out var size))
                {
                    return size;
                }

                if (TryGetSizeForStruct(field, out var sizeForField))
                {
                    return sizeForField;
                }
            }

            return 5;
        }

        private static bool TryGetSizeForStruct(FieldInfo field, out int sizeForField)
        {
            if (structSizeDictionary.TryGetValue(field.FieldType, out sizeForField))
            {
                return true;
            }

            if (field.FieldType.IsValueType && GetFields(field.FieldType).Count <= 1)
            {
                var result = (int) typeof(Unsafe).GetMethod("SizeOf").MakeGenericMethod(field.FieldType)
                    .Invoke(null, Array.Empty<Type>());

                structSizeDictionary.Add(field.FieldType, result);
                {
                    sizeForField = result;
                    return true;
                }
            }

            sizeForField = 5;
            return false;
        }

        private static DictionarySlim<Type, Type> _collections = new DictionarySlim<Type, Type>();

        internal static Type GetCustomCollectionBaseCollection(Type type)
        {
            if (_collections.TryGetValue(type, out var result))
            {
                return result;
            }

            return null;
        }

        internal static List<FieldInfo> GetFields(Type type)
        {
            lock (_cacheLock)
            {
                var originalType = type;
                ref var fields = ref _cache.GetOrAddValueRef(type);
                if (fields == null)
                {
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

                        type = type.BaseType;
                    }

                    if (FieldInfoModifier.MustUseReflectionToSetReadonly)
                    {
                        fields = start.OrderBy(x => IsPrimitive(x) ? 0 : 1)
                            .ThenBy(x => x.IsInitOnly ? 0 : 1)
                            .ThenBy(x => x.FieldType == typeof(string) ? 0 : 1)
                            .ThenBy(x => x.Name).ToList();
                    }
                    else
                    {
                        fields = start.OrderBy(x => IsPrimitive(x) ? 0 : 1)
                            .ThenBy(x => x.FieldType == typeof(string) ? 0 : 1)
                            .ThenBy(x => x.Name).ToList();
                    }
                }

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
