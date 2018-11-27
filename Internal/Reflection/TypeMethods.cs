using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Apex.Serialization.Attributes;

namespace Apex.Serialization.Internal.Reflection
{
    internal static class TypeMethods
    {
        private static DictionarySlim<Type, List<MethodInfo>> _cache = new DictionarySlim<Type, List<MethodInfo>>();

        private static object _cacheLock = new object();

        internal static List<MethodInfo> GetAfterDeserializeMethods(Type type)
        {
            lock (_cacheLock)
            {
                ref var fields = ref _cache.GetOrAddValueRef(type);
                if (fields == null)
                {
                    var start = Enumerable.Empty<MethodInfo>();
                    while (type != null)
                    {
                        var newFields = type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                       BindingFlags.NonPublic |
                                                       BindingFlags.DeclaredOnly)
                            .Where(x => x.CustomAttributes.Any(a => a.AttributeType == typeof(AfterDeserialization)));

                        start = start.Concat(newFields);
                        type = type.BaseType;
                    }

                    fields = start.Reverse().ToList();
                }

                return fields;
            }
        }
    }
}
