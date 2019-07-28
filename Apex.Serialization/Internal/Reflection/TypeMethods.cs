using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Apex.Serialization.Attributes;

namespace Apex.Serialization.Internal.Reflection
{
    internal static class TypeMethods
    {
        private static DictionarySlim<Type, List<MethodInfo>> _deserializeCache = new DictionarySlim<Type, List<MethodInfo>>();

        private static object _deserializeCacheLock = new object();

        private static DictionarySlim<Type, List<MethodInformation>> _cache = new DictionarySlim<Type, List<MethodInformation>>();

        private static object _cacheLock = new object();

        internal static List<MethodInfo> GetAfterDeserializeMethods(Type type)
        {
            lock (_deserializeCacheLock)
            {
                ref var methods = ref _deserializeCache.GetOrAddValueRef(type);
                if (methods == null)
                {
                    var start = Enumerable.Empty<MethodInfo>();
                    while (type != null)
                    {
                        var newMethods = type.GetMethods(
                            BindingFlags.Instance
                            | BindingFlags.Static
                            | BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.DeclaredOnly)
                            .Where(x => x.CustomAttributes.Any(a => a.AttributeType == typeof(AfterDeserialization))
                                && (
                                    x.GetParameters().Length <= 1 && !x.IsStatic
                                    || x.GetParameters().Length <= 2 && x.GetParameters().Length >= 1 && x.IsStatic
                                    ));

                        start = start.Concat(newMethods);
                        type = type.BaseType;
                    }

                    methods = start.Reverse().ToList();
                }

                return methods;
            }
        }

        internal class MethodInformation
        {
            public readonly MethodInfo MethodInfo;
            public readonly Type[] ParameterTypes;
            public readonly Type[] GenericArguments;

            public MethodInformation(MethodInfo methodInfo, Type[] parameterTypes, Type[] genericArguments)
            {
                MethodInfo = methodInfo;
                ParameterTypes = parameterTypes;
                GenericArguments = genericArguments;
            }
        }

        internal static List<MethodInformation> GetMethods(Type type)
        {
            lock (_cacheLock)
            {
                ref var methods = ref _cache.GetOrAddValueRef(type);
                if (methods == null)
                {
                    methods = new List<MethodInformation>();
                    var methodInfos = type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                    BindingFlags.NonPublic |
                                                    BindingFlags.DeclaredOnly | BindingFlags.Static).ToList();
                    for (int i = 0; i < methodInfos.Count; ++i)
                    {
                        methods.Add(new MethodInformation(
                            methodInfos[i],
                            methodInfos[i].GetParameters().Select(x => x.ParameterType).ToArray(),
                            methodInfos[i].GetGenericArguments()
                            ));
                    }
                }

                return methods;
            }
        }
    }
}
