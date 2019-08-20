using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

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
            if (t.IsInterface || t.IsAbstract)
            {
                return false;
            }

            return _isSealedOrHasNoDescendantsMap.GetOrAdd(t, k =>
            {
                foreach (var assembly in AllAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.DefinedTypes)
                        {
                            if (type.IsSubclassOf(t))
                            {
                                return false;
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                    }
                }

                return true;
            });
        }

        private static HashSet<Assembly>? _allAssemblies;
        private static object _allAssembliesLock = new object();

        private static IEnumerable<Assembly> AllAssemblies()
        {
            if (_allAssemblies != null)
            {
                return _allAssemblies;
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

                _allAssemblies = allAssemblies;
                return allAssemblies;
            }
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

        internal static bool CannotReferenceSelf(Type type)
        {
            var testedTypes = new HashSet<Type>();
            return CannotReference(type, type, testedTypes);
        }

        private static bool CannotReference(Type originalType, Type currentType, HashSet<Type> testedTypes)
        {
            if (!testedTypes.Add(currentType))
            {
                return true;
            }

            var fields = TypeFields.GetOrderedFields(currentType);

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

                if (!CannotReference(originalType, fieldType, testedTypes))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
