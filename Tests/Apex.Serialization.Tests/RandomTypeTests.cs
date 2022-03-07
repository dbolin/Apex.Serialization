using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class RandomTypeTests : AbstractSerializerTestBase
    {
        public ModuleBuilder ModuleBuilder { get; }

        public RandomTypeTests()
        {
            var assemblyName = new AssemblyName("Apex.Serialization.Tests.GeneratedTypes");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
            ModuleBuilder = assemblyBuilder.DefineDynamicModule("Main");
            _modifySettings = s =>
            {
                s.MarkSerializable(x => x.Assembly.GetName().Name == "Apex.Serialization.Tests.GeneratedTypes");
                s.InliningMaxDepth = 3;
            };
        }

        [Fact]
        public void Test1()
        {
            var r = new Random(12242881);

            for (int i = 0; i < 40; ++i)
            {
                var isGraph = i % 2 == 1;
                var typeBuilder = GenerateTypeBuilder(r, isGraph);
                var obj = BuildObject(r, typeBuilder, isGraph);
                RoundTrip(obj, isGraph ? s => s.SerializationMode == Mode.Graph : (Func<Settings, bool>?)null);
            }
        }

        private readonly List<Type> _predefinedTypes = new List<Type>
        {
            typeof(int),
            typeof(decimal),
            typeof(Guid),
            typeof(string),
        };

        private readonly List<TypeBuilder> _typeBuilders = new List<TypeBuilder>();

        private TypeBuilder GenerateTypeBuilder(Random r, bool isGraph)
        {
            // TODO: add generic types
            var isStruct = r.Next() % 16 == 0;
            var isSealed = r.Next() % 1 == 0 && !isStruct ? TypeAttributes.Sealed : TypeAttributes.Class;
            var typeBuilder = ModuleBuilder.DefineType(
                GetName(r),
                TypeAttributes.Public | isSealed,
                isStruct ? typeof(ValueType) : typeof(object)
                );
            _typeBuilders.Add(typeBuilder);

            var numberOfFields = r.Next(1, 8);

            for (int i = 0; i < numberOfFields; ++i)
            {
                var isReadonly = r.Next() % 1 == 0 ? FieldAttributes.InitOnly : 0;
                var type = GetTypeForField(r);
                typeBuilder.DefineField(GetName(r), type, FieldAttributes.Public | isReadonly);
            }

            return typeBuilder;
        }

        private Type GetTypeForField(Random r)
        {
            // TODO: add Nullable<>
            var total = _predefinedTypes.Count + _typeBuilders.Count;
            var n = r.Next(0, total - 1);
            if(n < _predefinedTypes.Count)
            {
                return _predefinedTypes[n];
            }

            return _typeBuilders[n - _predefinedTypes.Count];
        }

        private string GetName(Random r)
        {
            var sb = new StringBuilder();
            var len = r.Next(6, 14);
            for (int i = 0; i < len; ++i)
            {
                sb.Append((char)r.Next('a','z'));
            }

            return sb.ToString();
        }

        private Dictionary<TypeBuilder, Type> _createdTypes = new Dictionary<TypeBuilder, Type>();
        private Dictionary<TypeBuilder, object> _createdObjects = new Dictionary<TypeBuilder, object>();

        private object BuildObject(Random r, TypeBuilder typeBuilder, bool isGraph)
        {
            if(isGraph && _createdObjects.TryGetValue(typeBuilder, out var existing))
            {
                return existing;
            }

            if (!_createdTypes.TryGetValue(typeBuilder, out var t))
            {
                t = typeBuilder.CreateType()!;

                if(t.IsValueType)
                {
                    AssertionOptions.AssertEquivalencyUsing(x => { x.GetType().GetMethod("ComparingByMembers", Array.Empty<Type>()).MakeGenericMethod(t).Invoke(x, null); return x; });
                }

                _createdTypes.Add(typeBuilder, t);
            }

            var result = Activator.CreateInstance(t!)!;

            if (isGraph)
            {
                _createdObjects.Add(typeBuilder, result);
            }

            PopulateObject(r, result, t, isGraph);

            return result;
        }

        private void PopulateObject(Random r, object result, Type t, bool isGraph)
        {
            foreach(var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if(_predefinedTypes.Contains(field.FieldType))
                {
                    if(field.FieldType == typeof(int))
                    {
                        field.SetValue(result, r.Next());
                    }
                    else if (field.FieldType == typeof(Guid))
                    {
                        field.SetValue(result, Guid.NewGuid());
                    }
                    else if(field.FieldType == typeof(string))
                    {
                        field.SetValue(result, GetName(r));
                    }
                    continue;
                }

                var obj = BuildObject(r, _typeBuilders.Single(x => field.FieldType.FullName == x.FullName), isGraph);
                field.SetValue(result, obj);
            }
        }
    }
}
