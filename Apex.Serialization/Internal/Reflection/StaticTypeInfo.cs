﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Apex.Serialization.Internal.Reflection
{
    internal static class StaticTypeInfo<T>
    {
        internal static bool IsSealedOrHasNoDescendents = typeof(T).IsSealed || StaticTypeInfo.HasNoDescendents(typeof(T));
        internal static bool IsValueType = typeof(T).IsValueType;
        internal static bool IsNullable = typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    internal static class StaticTypeInfo
    {
        private static ConcurrentDictionary<Type, bool> _isSealedOrHasNoDescendantsMap = new ConcurrentDictionary<Type, bool>();

        internal static bool IsSealedOrHasNoDescendents(Type t) => t.IsSealed || HasNoDescendents(t);
        internal static bool HasNoDescendents(Type t)
        {
            if (t.IsInterface || t.IsAbstract || typeof(Delegate).IsAssignableFrom(t))
            {
                return false;
            }

            return _isSealedOrHasNoDescendantsMap.GetOrAdd(t, k =>
            {
                if(t == typeof(object))
                {
                    return false;
                }

                return !GetTypesWithDescendents().Contains(t);
            }
            );
        }

        private static HashSet<Type>? _allTypes;
        private static HashSet<Type>? _typesWithDescendents;
        private static object _allAssembliesLock = new object();

        private static HashSet<Type> AllTypes()
        {
            if (_allTypes != null)
            {
                return _allTypes;
            }

            lock (_allAssembliesLock)
            {
                var entry = Assembly.GetEntryAssembly();
                var allAssemblies = new HashSet<Assembly>();
                Add(allAssemblies, entry);

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    allAssemblies.Add(assembly);
                }

                _allTypes = GetAllTypesFrom(allAssemblies);
                return _allTypes;
            }
        }

        private static HashSet<Type> GetTypesWithDescendents()
        {
            if(_typesWithDescendents != null)
            {
                return _typesWithDescendents;
            }

            lock(_allAssembliesLock)
            {
                _typesWithDescendents = GetAllTypesWithDescendents(AllTypes());
                return _typesWithDescendents;
            }
        }

        private static HashSet<Type> GetAllTypesWithDescendents(HashSet<Type> allTypes)
        {
            var result = new HashSet<Type>();
            foreach(var type in allTypes)
            {
                if(type.BaseType != null)
                {
                    result.Add(type.BaseType);
                }
            }

            return result;
        }

        private static HashSet<Type> GetAllTypesFrom(HashSet<Assembly> allAssemblies)
        {
            var result = new HashSet<Type>();
            foreach (var assembly in allAssemblies)
            {
                try
                {
                    foreach (var type in assembly.DefinedTypes)
                    {
                        if(type.BaseType == typeof(object))
                        {
                            continue;
                        }

                        result.Add(type);
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    if(e.Types != null)
                    {
                        foreach(var type in e.Types)
                        {
                            if(type == null)
                            {
                                continue;
                            }

                            if (type.BaseType == typeof(object))
                            {
                                continue;
                            }

                            result.Add(type);
                        }
                    }
                }
            }

            return result;
        }

        private static void Add(HashSet<Assembly> allAssemblies, Assembly? initial)
        {
            if (initial == null)
            {
                return;
            }

            if (allAssemblies.Add(initial))
            {
                foreach (var referencedAssemblyName in initial.GetReferencedAssemblies())
                {
                    try
                    {
                        var assembly = Assembly.Load(referencedAssemblyName);
                        Add(allAssemblies, assembly);
                    }
                    catch (FileNotFoundException)
                    { }
                    catch (FileLoadException)
                    { }
                }
            }
        }

        internal static bool CannotReferenceSelf(Type type, ImmutableSettings settings)
        {
            var testedTypes = new HashSet<Type>();
            return CannotReference(type, type, testedTypes, settings);
        }

        private static bool CannotReference(Type originalType, Type currentType, HashSet<Type> testedTypes, ImmutableSettings settings)
        {
            if (!testedTypes.Add(currentType))
            {
                return true;
            }

            if (currentType.IsArray)
            {
                var elementType = currentType.GetElementType();
                if (elementType == null)
                {
                    // should never happen, but return false since this is unknown behavior
                    return false;
                }
                if (!HasNoDescendents(elementType))
                {
                    return false;
                }
                return CannotReference(originalType, elementType, testedTypes, settings);
            }

            var fields = TypeFields.GetOrderedFields(currentType, settings);

            foreach (var field in fields)
            {
                if (TypeFields.IsPrimitive(field.FieldType))
                {
                    continue;
                }

                var fieldType = field.FieldType;

                if (fieldType == typeof(string))
                {
                    continue;
                }

                if (!HasNoDescendents(fieldType))
                {
                    return false;
                }

                if (fieldType.IsAssignableFrom(originalType))
                {
                    return false;
                }

                if (!CannotReference(originalType, fieldType, testedTypes, settings))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
