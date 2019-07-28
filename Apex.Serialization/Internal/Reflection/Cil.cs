using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Apex.Serialization.Internal.Reflection
{
    internal static class Cil
    {
        public static ConstructorInfo FindEmptyDeserializationConstructor(Type type)
        {
            try
            {
                var module = ModuleDefinition.ReadModule(type.Assembly.Location);
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
            return allConstructors.Single(x => x.GetParameters().Length == 0);
        }

        public static (ConstructorInfo constructor, List<int> fieldOrder)? FindSpecificDeserializationConstructor(Type type, List<FieldInfo> fields)
        {
            if(fields.Count == 0)
            {
                return null;
            }

            // we need to find a constructor that, when combined with the base constructor, assigns values to all fields
            // and does nothing else

            try
            {
                var module = ModuleDefinition.ReadModule(type.Assembly.Location);
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

        private static List<int> ConstructorMatchesFields(TypeReference typeRef, MethodDefinition method, List<FieldInfo> fields)
        {
            if(method.Parameters.Count != fields.Count)
            {
                return null;
            }

            var fieldsToMatch = new HashSet<FieldInfo>(fields);

            foreach(var methodParam in method.Parameters)
            {
                bool found = false;
                foreach(var field in fieldsToMatch)
                {
                    var t1 = GetResolvedParameter(typeRef, methodParam.ParameterType);
                    var t2 = field.FieldType;
                    if(t1.Module.Assembly.FullName == t2.Assembly.FullName
                        && t1.FullName == t2.FullName)
                    {
                        found = true;
                        fieldsToMatch.Remove(field);
                        break;
                    }
                }

                if(!found)
                {
                    return null;
                }
            }

            // now read the method IL and match the fields stored to to the parameters
            var il = method.Body.Instructions;

            var index = 0;
            bool arg0Loaded = false;
            bool paramLoaded = false;
            int paramLoadedIndex = 0;
            var result = new List<(int paramIndex, FieldDefinition fieldDef)>();
            var usedFields = new HashSet<FieldInfo>();
            while(index < il.Count)
            {
                var instruction = il[index++];

                if(instruction.OpCode.Code == Code.Ret)
                {
                    if(result.Count != fields.Count || usedFields.Count != fields.Count)
                    {
                        return null;
                    }

                    return result.OrderBy(x => x.paramIndex).Select(x => fields.FindIndex(f => f.Name == x.fieldDef.Name)).ToList();
                }

                if(instruction.OpCode.Code == Code.Nop)
                {
                    continue;
                }

                if(instruction.OpCode.Code == Code.Ldarg_0)
                {
                    if(arg0Loaded)
                    {
                        return null;
                    }

                    arg0Loaded = true;
                    continue;
                }

                if(instruction.OpCode.Code == Code.Ldarg_1)
                {
                    if(paramLoaded)
                    {
                        return null;
                    }

                    paramLoaded = true;
                    paramLoadedIndex = 0;
                    continue;
                }

                if (instruction.OpCode.Code == Code.Ldarg_2)
                {
                    if (paramLoaded)
                    {
                        return null;
                    }

                    paramLoaded = true;
                    paramLoadedIndex = 1;
                    continue;
                }

                if (instruction.OpCode.Code == Code.Ldarg_3)
                {
                    if (paramLoaded)
                    {
                        return null;
                    }

                    paramLoaded = true;
                    paramLoadedIndex = 2;
                    continue;
                }

                if (instruction.OpCode.Code == Code.Ldarg_S)
                {
                    if (paramLoaded)
                    {
                        return null;
                    }

                    paramLoaded = true;
                    paramLoadedIndex = method.Parameters.IndexOf((ParameterDefinition)instruction.Operand);
                    continue;
                }

                if (instruction.OpCode.Code == Code.Stfld)
                {
                    if(!arg0Loaded || !paramLoaded)
                    {
                        return null;
                    }

                    arg0Loaded = false;
                    paramLoaded = false;

                    var fieldRef = instruction.Operand as FieldReference;
                    var fieldDef = fieldRef != null ? fieldRef.Resolve() : (FieldDefinition)instruction.Operand;
                    var field = fields.Single(x => x.Name == fieldDef.Name);
                    result.Add((paramLoadedIndex, fieldDef));
                    usedFields.Add(field);
                    continue;
                }

                if(instruction.OpCode.Code == Code.Call)
                {
                    if(!arg0Loaded || paramLoaded)
                    {
                        return null;
                    }

                    arg0Loaded = false;
                    continue;
                }

                return null;
            }

            return null;
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

                if (t1.Assembly.FullName != t2.Module.Assembly.FullName
                    || t1.FullName != t2.FullName)
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

            var constructorCalled = baseConstructorCall.Operand as MethodReference;
            if (constructorCalled == null)
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
