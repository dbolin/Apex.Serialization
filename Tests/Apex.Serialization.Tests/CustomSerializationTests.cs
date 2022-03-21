using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Serialization.Extensions;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class CustomSerializationTests
    {
        public class Test
        {
            public Test? Nested;
            public int Value;

            public static void Serialize(Test t, IBinaryWriter writer)
            {
                writer.Write(t.Value - 1);
                if (t.Nested != null)
                {
                    writer.Write<byte>(1);
                    writer.WriteObject(t.Nested);
                } else
                {
                    writer.Write<byte>(0);
                }
            }

            public static void Deserialize(Test t, IBinaryReader reader)
            {
                t.Value = reader.Read<int>();
                var isNotNullByte = reader.Read<byte>();
                if (isNotNullByte == 1)
                {
                    t.Nested = reader.ReadObject<Test>();
                }
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

        public class TestWithHashSet
        {
            public readonly HashSet<Test> values = new HashSet<Test>();
        }

        [Fact]
        public void SimpleTest()
        {
            var settings = new Settings { SupportSerializationHooks = true }
                .RegisterCustomSerializer<Test>(Test.Serialize, Test.Deserialize)
                .MarkSerializable(typeof(Test));

            var binary = Binary.Create(settings);
            var m = new MemoryStream();

            var x = new Test {Value = 10, Nested = new Test { Value = 9 } };

            binary.Write(x, m);

            m.Seek(0, SeekOrigin.Begin);

            var y = binary.Read<Test>(m);

            y.Value.Should().Be(x.Value - 1);
            y.Nested.Value.Should().Be(x.Nested.Value - 1);
        }

        [Fact]
        public void HashSetTest()
        {
            var settings = new Settings { SupportSerializationHooks = true }
                .RegisterCustomSerializer<HashSet<Test>>((o, s) =>
                {
                    s.Write(o.Count);
                    foreach (var i in o)
                    {
                        s.WriteObject(i);
                    }
                }, (o, s) =>
                {
                    var count = s.Read<int>();
                    o.EnsureCapacity(count);
                    for (int i = 0; i < count; ++i)
                    {
                        o.Add(s.ReadObject<Test>());
                    }
                })
                .MarkSerializable(typeof(TestWithHashSet))
                .MarkSerializable(typeof(HashSet<Test>))
                .MarkSerializable(typeof(Test));

            var binary = Binary.Create(settings);
            var m = new MemoryStream();

            var x = new TestWithHashSet();
            x.values.Add(new Test { Value = 123 });

            binary.Write(x, m);

            m.Seek(0, SeekOrigin.Begin);

            var y = binary.Read<TestWithHashSet>(m);

            y.values.First().Value.Should().Be(123);
        }

        [Fact]
        public void CustomContextTest()
        {
            var settings = new Settings { SupportSerializationHooks = true }
                .RegisterCustomSerializer<TestCustomContext, CustomContext>(TestCustomContext.Serialize, TestCustomContext.Deserialize)
                .MarkSerializable(typeof(TestCustomContext));

            var binary = Binary.Create(settings);
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
            var settings = new Settings { SupportSerializationHooks = true }
                .MarkSerializable(typeof(CustomSerializationTests));
            var binary = Binary.Create(settings);

            binary.Precompile<CustomSerializationTests>();
            binary.Precompile(typeof(CustomSerializationTests));
        }

        private class OpenGeneric<T> { }

        [Fact]
        public void PrecompileOpenGeneric()
        {
            var settings = new Settings { SupportSerializationHooks = true }
                .MarkSerializable(typeof(OpenGeneric<>));
            var binary = Binary.Create(settings);

            Assert.Throws<ArgumentException>(() => binary.Precompile(typeof(OpenGeneric<>)));
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
            var settings = new Settings { SupportSerializationHooks = true }
                .RegisterCustomSerializer<TestWithConstructor>(TestWithConstructor.Serialize, TestWithConstructor.Deserialize)
                .MarkSerializable(typeof(TestWithConstructor));

            var binary = Binary.Create(settings);
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
            var settings = new Settings { SupportSerializationHooks = true }
                .RegisterCustomSerializer<TestWithConstructor>(TestWithConstructor.Serialize, TestWithConstructor.Deserialize)
                .MarkSerializable(typeof(TestWithConstructor))
                .MarkSerializable(typeof(List<>));

            var binary = Binary.Create(settings);
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
