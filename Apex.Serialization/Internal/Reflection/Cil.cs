using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Apex.Serialization.Internal.Reflection
{
    internal static class Cil
    {
        private static readonly ConcurrentDictionary<string, ModuleDefinition> _moduleDefinitions = new ConcurrentDictionary<string, ModuleDefinition>();

        public static ConstructorInfo? FindEmptyDeserializationConstructor(Type type)
        {
            if (type.Assembly.IsDynamic)
            {
                return null;
            }

            try
            {
                var module = _moduleDefinitions.GetOrAdd(type.Assembly.Location, k => ModuleDefinition.ReadModule(k));
                var typeRef = module.ImportReference(type);
                var typeDef = typeRef.Resolve();
                var defaultCtor = typeDef.Methods.FirstOrDefault(x => x.IsConstructor && x.Parameters.Count == 0 && !x.IsStatic);
                if (defaultCtor == null)
                {
                    return null;
                }

                if (!IsEmptyConstructor(defaultCtor))
                {
                    return null;
                }
            }
            catch
            {
#if DEBUG
                throw;
#else
                return null;
#endif
            }

            var allConstructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
#if DEBUG
            TypesUsingEmptyConstructor.TryAdd(type, true);
#endif
            return allConstructors.Single(x => x.GetParameters().Length == 0);
        }

#if DEBUG
        internal static ConcurrentDictionary<Type, bool> TypesUsingEmptyConstructor = new ConcurrentDictionary<Type, bool>();
        internal static bool TypeUsesEmptyConstructor(Type t) => TypesUsingEmptyConstructor.ContainsKey(t);
        internal static ConcurrentDictionary<Type, bool> TypesUsingConstructor = new ConcurrentDictionary<Type, bool>();
        internal static bool TypeUsesFullConstructor(Type t) => TypesUsingConstructor.ContainsKey(t);
#endif

        public static (ConstructorInfo constructor, List<int> fieldOrder)? FindSpecificDeserializationConstructor(Type type, List<FieldInfo> fields)
        {
            if(fields.Count == 0 || type.Assembly.IsDynamic)
            {
                return null;
            }

            // we need to find a constructor that, when combined with the base constructor, assigns values to all fields
            // and does nothing else

            try
            {
                var module = _moduleDefinitions.GetOrAdd(type.Assembly.Location, k => ModuleDefinition.ReadModule(k));
                var typeRef = module.ImportReference(type);
                var typeDef = typeRef.Resolve();
                var constructors = typeDef.Methods.Where(x => x.IsConstructor && !x.IsStatic);

                foreach (var method in constructors)
                {
                    var fieldOrder = ConstructorMatchesFields(typeRef, method, fields);
                    if (fieldOrder != null)
                    {
                        var allConstructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        foreach (var constructor in allConstructors)
                        {
                            if (Matches(typeRef, constructor, method))
                            {
#if DEBUG
                                TypesUsingConstructor.TryAdd(type, true);
#endif
                                return (constructor, fieldOrder);
                            }
                        }

                        return null;
                    }
                }

                return null;
            }
            catch
            {
#if DEBUG
                throw;
#else
                return null;
#endif
            }
        }

        private static List<int>? ConstructorMatchesFields(TypeReference typeRef, MethodDefinition method, List<FieldInfo> fields)
        {
            if (method.Parameters.Count != fields.Count)
            {
                return null;
            }

            var fieldsToMatch = new HashSet<FieldInfo>(fields);

            foreach (var methodParam in method.Parameters)
            {
                bool found = false;
                foreach (var field in fieldsToMatch)
                {
                    var t1 = GetResolvedParameter(typeRef, methodParam.ParameterType);
                    var t2 = field.FieldType;
                    if (TypesMatch(t1, t2))
                    {
                        found = true;
                        fieldsToMatch.Remove(field);
                        break;
                    }
                }

                if (!found)
                {
                    return null;
                }
            }

            // now read the method IL and match the fields stored to to the parameters
            var il = method.Body.Instructions;

            var index = 0;
            bool baseConstructorCalled = false;
            bool arg0Loaded = false;
            bool paramLoaded = false;
            int paramLoadedIndex = 0;
            var result = new List<(int paramIndex, FieldDefinition fieldDef)>();
            var usedFields = new HashSet<FieldInfo>();
            while (index < il.Count)
            {
                var instruction = il[index++];

                if (instruction.OpCode.Code == Code.Ret)
                {
                    if (result.Count != fields.Count || usedFields.Count != fields.Count)
                    {
                        return null;
                    }

                    return result.OrderBy(x => x.paramIndex).Select(x => fields.FindIndex(f => f.Name == x.fieldDef.Name)).ToList();
                }

                if (instruction.OpCode.Code == Code.Nop)
                {
                    continue;
                }

                if (instruction.OpCode.Code == Code.Ldarg_0)
                {
                    if (arg0Loaded || paramLoaded)
                    {
                        return null;
                    }

                    arg0Loaded = true;
                    continue;
                }

                if (instruction.OpCode.Code == Code.Ldarg_1)
                {
                    if (paramLoaded || !arg0Loaded)
                    {
                        return null;
                    }

                    paramLoaded = true;
                    paramLoadedIndex = 0;
                    continue;
                }

                if (instruction.OpCode.Code == Code.Ldarg_2)
                {
                    if (paramLoaded || !arg0Loaded)
                    {
                        return null;
                    }

                    paramLoaded = true;
                    paramLoadedIndex = 1;
                    continue;
                }

                if (instruction.OpCode.Code == Code.Ldarg_3)
                {
                    if (paramLoaded || !arg0Loaded)
                    {
                        return null;
                    }

                    paramLoaded = true;
                    paramLoadedIndex = 2;
                    continue;
                }

                if (instruction.OpCode.Code == Code.Ldarg_S)
                {
                    if (paramLoaded || !arg0Loaded)
                    {
                        return null;
                    }

                    paramLoaded = true;
                    paramLoadedIndex = method.Parameters.IndexOf((ParameterDefinition)instruction.Operand);
                    continue;
                }

                if (instruction.OpCode.Code == Code.Stfld)
                {
                    if (!arg0Loaded || !paramLoaded)
                    {
                        return null;
                    }

                    arg0Loaded = false;
                    paramLoaded = false;

                    var fieldDef = instruction.Operand is FieldReference fieldRef
                        ? fieldRef.Resolve()
                        : (FieldDefinition)instruction.Operand;
                    var field = fields.SingleOrDefault(x => x.Name == fieldDef.Name);
                    if (field == null)
                    {
                        return null;
                    }

                    result.Add((paramLoadedIndex, fieldDef));
                    usedFields.Add(field);
                    continue;
                }

                if (instruction.OpCode.Code == Code.Call)
                {
                    if (arg0Loaded && paramLoaded)
                    {
                        // check for property set
                        if (!(instruction.Operand is MethodDefinition propertyMethodDef))
                        {
                            return null;
                        }

                        var instructions = propertyMethodDef.Body?.Instructions;
                        if (instructions == null)
                        {
                            return null;
                        }

                        var filteredInstructions = instructions.Where(x => x.OpCode.Code != Code.Nop && x.OpCode.Code != Code.Ret).ToList();
                        if (filteredInstructions.Count != 3)
                        {
                            return null;
                        }

                        if (filteredInstructions[0].OpCode.Code != Code.Ldarg_0)
                        {
                            return null;
                        }

                        if (filteredInstructions[1].OpCode.Code != Code.Ldarg_1)
                        {
                            return null;
                        }

                        if (filteredInstructions[2].OpCode.Code != Code.Stfld)
                        {
                            return null;
                        }

                        arg0Loaded = false;
                        paramLoaded = false;
                        var stFld = filteredInstructions[2];

                        var fieldDef = stFld.Operand is FieldReference fieldRef
                            ? fieldRef.Resolve()
                            : (FieldDefinition)stFld.Operand;
                        var field = fields.SingleOrDefault(x => x.Name == fieldDef.Name);
                        if (field == null)
                        {
                            return null;
                        }

                        result.Add((paramLoadedIndex, fieldDef));
                        usedFields.Add(field);
                        continue;
                    }

                    if (!arg0Loaded || paramLoaded || baseConstructorCalled)
                    {
                        return null;
                    }

                    var methodDef = instruction.Operand is MethodReference methodRef
                        ? methodRef.Resolve() :
                        (MethodDefinition)instruction.Operand;
                    if (!IsEmptyConstructor(methodDef))
                    {
                        return null;
                    }

                    arg0Loaded = false;
                    baseConstructorCalled = true;
                    continue;
                }

                return null;
            }

            return null;
        }

        private static bool TypesMatch(TypeDefinition t1, Type t2)
        {
            if(t1.Module.Assembly.FullName != t2.Assembly.FullName)
            {
                return false;
            }

            if(t1.Name != t2.Name)
            {
                return false;
            }

            if(t1.DeclaringType != null)
            {
                if(t2.DeclaringType == null)
                {
                    return false;
                }

                return t1.Name == t2.Name && TypesMatch(t1.DeclaringType, t2.DeclaringType);
            }

            return t1.Namespace == t2.Namespace;
        }

        private static TypeDefinition GetResolvedParameter(TypeReference typeRef, TypeReference parameterType)
        {
            if(parameterType.IsGenericParameter)
            {
                var genericType = (GenericInstanceType)typeRef;

                var openGeneric = genericType.ElementType;

                var genericArgumentIndex = openGeneric.GenericParameters.Select((x, i) => new { x, i }).Single(x => x.x.Name == parameterType.Name).i;
                var genericArgument = genericType.GenericArguments[genericArgumentIndex];

                return genericArgument.Resolve();
            }

            return parameterType.Resolve();
        }

        private static bool Matches(TypeReference typeRef, ConstructorInfo constructor, MethodDefinition method)
        {
            var p1 = constructor.GetParameters();
            var p2 = method.Parameters;
            if (p1.Length != p2.Count)
            {
                return false;
            }

            for (int i = 0; i < p1.Length; ++i)
            {
                var t1 = p1[i].ParameterType;
                var t2 = GetResolvedParameter(typeRef, p2[i].ParameterType);

                if (!TypesMatch(t2, t1))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsEmptyConstructor(MethodDefinition ctor)
        {
            if(ctor.Body.Instructions.Any(x =>
                x.OpCode.Code != Code.Nop
                && x.OpCode.Code != Code.Ret
                && x.OpCode.Code != Code.Ldarg_0
                && x.OpCode.Code != Code.Call))
            {
                return false;
            }

            var methodCalls = ctor.Body.Instructions.Where(x => x.OpCode.Code == Code.Call).ToList();

            if(methodCalls.Count == 0)
            {
                return true;
            }

            if(methodCalls.Count > 1)
            {
                return false;
            }

            var baseConstructorCall = methodCalls[0];

            if (baseConstructorCall == null)
            {
                return false;
            }

            if (!(baseConstructorCall.Operand is MethodReference constructorCalled))
            {
                return false;
            }

            var definition = constructorCalled.Resolve();

            if(definition == null)
            {
                return false;
            }

            if (definition.Parameters.Count > 0 || !IsEmptyConstructor(definition))
            {
                return false;
            }

            return true;
        }
    }
}
