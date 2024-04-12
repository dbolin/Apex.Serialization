using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Apex.Serialization.Extensions;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class CustomSerializationTests : AbstractSerializerTestBase
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
            public HashSet<Test> values = new HashSet<Test>();
        }

        public CustomSerializationTests()
        {
            _modifySettings = s =>
            {
                s.RegisterCustomSerializer<Test>(Test.Serialize, Test.Deserialize);
                s.RegisterCustomSerializer<HashSet<Test>>((o, s) =>
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
                });
                s.RegisterCustomInstantiator<HashSet<Test>>(s =>
                    new HashSet<Test>(new TestEqualityComparer())
                );

                s.RegisterCustomSerializer(typeof(Stack<>), typeof(CustomSerializationTests), "WriteStack", "ReadStack");
                s.RegisterCustomInstantiator(typeof(Stack<>), typeof(CustomSerializationTests), "CreateStack");

                // For testing failures
                s.RegisterCustomSerializer<HashSet<TestWithConstructor>>((o, s) =>
                {
                    s.Write(o.Count);
                    foreach (var i in o)
                    {
                        s.WriteObject(i);
                    }
                },
                (o, s) =>
                {
                });
                s.RegisterCustomInstantiator<HashSet<TestWithConstructor>>(s =>
                {
                    var res = new HashSet<TestWithConstructor>();

                    var count = s.Read<int>();
                    res.EnsureCapacity(count);
                    for (int i = 0; i < count; ++i)
                    {
                        res.Add(s.ReadObject<TestWithConstructor>());
                    }

                    return res;
                }
                );
            };
        }

        private static void WriteStack<T>(Stack<T> obj, IBinaryWriter writer)
        {
            writer.Write(obj.Count);
            foreach (var i in obj.Reverse())
            {
                writer.WriteObject(i);
            }
        }

        private static void ReadStack<T>(Stack<T> obj, IBinaryReader reader)
        {
            var count = reader.Read<int>();
            for (int i = 0; i < count; ++i)
            {
                obj.Push(reader.ReadObject<T>());
            }
        }

        private static Stack<T> CreateStack<T>(IBinaryReader reader)
        {
            return new Stack<T>();
        }

        [Fact]
        public void SimpleTest()
        {
            var x = new Test {Value = 10 };

            RoundTrip(x, (a, b) => a.Value == b.Value + 1);

            x = new Test { Value = 10, Nested = new Test { Value = 9 } };

            RoundTrip(x, (a, b) => a.Value == b.Value + 1 && a.Nested.Value == b.Nested.Value + 1);
        }

        [Fact]
        public void HashSetTest()
        {
            var x = new TestWithHashSet();
            x.values = new HashSet<Test>(new TestEqualityComparer());
            x.values.Add(new Test { Value = 123 });

            RoundTrip(x, (a, b) => a.values.First().Value == b.values.First().Value + 1 && a.values.Comparer.GetType() == b.values.Comparer.GetType());
        }

        [Fact]
        public void SavedReferenceOrder()
        {
            var x = new TestWithHashSet();
            var t = new Test { Value = 123 };
            x.values.Add(t);
            var x2 = new TestWithHashSet();
            x2.values.Add(t);
            var y = new
            {
                x = x,
                x2 = x2
            };

            RoundTrip(y, (a, b) => a.x.values.First().Value == b.x.values.First().Value + 1);
        }

        [Fact]
        public void CustomInstantiationThrowsWhenReadingObjectReference()
        {
            var x = new
            {
                h = new HashSet<TestWithConstructor>() { new TestWithConstructor(1) }
            };

            // Tree mode should work
            RoundTrip(x, (a, b) => a.h.Count == b.h.Count, s => s.SerializationMode == Mode.Tree);

            // Graph mode should throw
            bool thrown = false;
            try
            {
                RoundTrip(x, (a, b) => a.h.Count == b.h.Count, s => s.SerializationMode == Mode.Graph);
            }
            catch (Exception ex)
            {
                ex.Message.Should().Be("Unable to read an object reference in graph mode during custom instantiation");
                thrown = true;
            }
            finally
            {
                thrown.Should().BeTrue();
            }
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
            var settings = new Settings { SupportSerializationHooks = true, AllowFunctionSerialization = true }
                .MarkSerializable(typeof(CustomSerializationTests));
            var binary = Binary.Create(settings);

            binary.Precompile<CustomSerializationTests>();
            binary.Precompile(typeof(CustomSerializationTests));
        }

        private class OpenGeneric<T> { }

        [Fact]
        public void PrecompileOpenGeneric()
        {
            var settings = new Settings { SupportSerializationHooks = true, AllowFunctionSerialization = true }
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

        [Fact]
        public void CustomGenericSerialization()
        {
            var s = new Stack<Test>();
            s.Push(new Test { Value = 1 });
            s.Push(new Test { Value = 5 });

            RoundTrip(s, (a, b) => a.Peek().Value == b.Peek().Value + 1);
        }

        private class TestEqualityComparer : IEqualityComparer<Test>
        {
            public bool Equals(Test? x, Test? y)
            {
                return EqualityComparer<Test>.Default.Equals(x, y);
            }

            public int GetHashCode([DisallowNull] Test obj)
            {
                return EqualityComparer<Test>.Default.GetHashCode(obj);
            }
        }
    }
}
