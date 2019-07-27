using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Linq;
using System.Reflection;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Apex.Serialization.Internal.Reflection
{
    internal static class Cil
    {
        private static readonly TypeReference _voidReference = ModuleDefinition.ReadModule(typeof(void).Module.Assembly.Location)
            .ImportReference(typeof(void));

        public static void AddEmptyConstructor(TypeDefinition type)
        {
            var methodAttributes = MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
            var method = new MethodDefinition(".ctor", methodAttributes, _voidReference);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            var methodReference = new MethodReference(".ctor", _voidReference, type.BaseType) { HasThis = true };
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, methodReference));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(method);
        }

        public static ConstructorInfo FindDeserializationConstructor(Type type)
        {
            var module = ModuleDefinition.ReadModule(type.Assembly.Location);
            var typeRef = module.ImportReference(type);
            var typeDef = typeRef.Resolve();
            var defaultCtor = typeDef.Methods.FirstOrDefault(x => x.IsConstructor && x.Parameters.Count == 0 && !x.IsStatic);
            if(defaultCtor == null)
            {
                return null;
            }

            if(!IsEmptyConstructor(defaultCtor))
            {
                return null;
            }

            var allConstructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return allConstructors.First(x => x.GetParameters().Length == 0);
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
