using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apex.Serialization.Extensions;
using Apex.Serialization.Internal;
using FluentAssertions;
using Xunit;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization.Tests
{
    public class CustomSerializationTests
    {
        public class Test
        {
            public int Value;

            public static void Serialize(Test t, IBinaryWriter writer)
            {
                writer.Write(t.Value - 1);
            }

            public static void Deserialize(Test t, IBinaryReader reader)
            {
                t.Value = reader.Read<int>();
            }
        }

        public class CustomContext
        {
            public int ValueOverride;
        }

        public class TestCustomContext
        {
            public int Value;

            public static void Serialize(TestCustomContext t, IBinaryWriter writer, CustomContext context)
            {
                writer.Write(context.ValueOverride);
            }

            public static void Deserialize(TestCustomContext t, IBinaryReader reader, CustomContext context)
            {
                t.Value = context.ValueOverride;
            }
        }

        [Fact]
        public void SimpleTest()
        {
            Binary.Instantiated = false;
            Binary.RegisterCustomSerializer<Test>(Test.Serialize, Test.Deserialize);

            var binary = Binary.Create(new Settings {SupportSerializationHooks = true});
            var m = new MemoryStream();

            var x = new Test {Value = 10};

            binary.Write(x, m);

            m.Seek(0, SeekOrigin.Begin);

            var y = binary.Read<Test>(m);

            y.Value.Should().Be(x.Value - 1);
        }

        [Fact]
        public void CustomContextTest()
        {
            Binary.Instantiated = false;
            Binary.RegisterCustomSerializer<TestCustomContext, CustomContext>(TestCustomContext.Serialize, TestCustomContext.Deserialize);

            var binary = Binary.Create(new Settings { SupportSerializationHooks = true });
            var m = new MemoryStream();

            var x = new TestCustomContext { Value = 10 };
            var context = new CustomContext { ValueOverride = 3 };

            binary.SetCustomHookContext(context);

            binary.Write(x, m);

            m.Seek(0, SeekOrigin.Begin);

            var y = binary.Read<TestCustomContext>(m);

            y.Value.Should().Be(3);
        }

        [Fact]
        public void Precompile()
        {
            var binary = Binary.Create(new Settings { SupportSerializationHooks = true });

            binary.Precompile(typeof(Settings));
            binary.Precompile<Settings>();
            binary.Precompile<CustomSerializationTests>();
        }

        public class TestWithConstructor
        {
            public int Value;

            public TestWithConstructor(int value)
            {
                Value = value;
            }

            public static void Serialize(TestWithConstructor t, IBinaryWriter writer)
            {
                writer.Write(t.Value - 1);
            }

            public static void Deserialize(TestWithConstructor t, IBinaryReader reader)
            {
                t.Value.Should().Be(0);
                t.Value = reader.Read<int>();
            }
        }

        [Fact]
        public void CustomSerializationShouldNotBePassedNull()
        {
            Binary.Instantiated = false;
            Binary.CustomActionSerializers.Remove(typeof(TestWithConstructor));
            Binary.CustomActionDeserializers.Remove(typeof(TestWithConstructor));
            Binary.RegisterCustomSerializer<TestWithConstructor>(TestWithConstructor.Serialize, TestWithConstructor.Deserialize);

            var binary = Binary.Create(new Settings { SupportSerializationHooks = true });
            var m = new MemoryStream();

            binary.Write<object>(new { A = (TestWithConstructor?)null }, m);

            m.Seek(0, SeekOrigin.Begin);

            var y = binary.Read<object>(m);
            TestWithConstructor? r = ((dynamic)y).A;
            r.Should().BeNull();
        }

        [Fact]
        public void CustomSerializationShouldAlwaysStartUninitialized()
        {
            Binary.Instantiated = false;
            Binary.CustomActionSerializers.Remove(typeof(TestWithConstructor));
            Binary.CustomActionDeserializers.Remove(typeof(TestWithConstructor));
            Binary.RegisterCustomSerializer<TestWithConstructor>(TestWithConstructor.Serialize, TestWithConstructor.Deserialize);

            var binary = Binary.Create(new Settings { SupportSerializationHooks = true });
            var m = new MemoryStream();

            var list = new List<TestWithConstructor>();
            for (int i = 0; i < 10; ++i)
            {
                list.Add(new TestWithConstructor(10));
            }

            binary.Write(list, m);

            m.Seek(0, SeekOrigin.Begin);

            var y = binary.Read<List<TestWithConstructor>>(m);

            foreach (var v in y)
            {
                v.Value.Should().Be(9);
            }
        }
    }
}
