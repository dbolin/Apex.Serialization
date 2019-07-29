using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class ConstructorTests : AbstractSerializerTestBase
    {
        public class Test
        {
            public Test()
            { }

            public Test(int v = 1)
            {
                Value = v + 3;
            }

            public Test(params object[] obj)
            {
                Value = obj.Length + 3;
            }

            [NonSerialized]
            public int Value;
        }

        private class Test2
        {
            public Test2(bool val1 = false, bool val2 = false)
            {
                Val1 = val1;
                Val2 = val2;
            }
            public bool Val1 { get; }
            public bool Val2 { get; }
        }

        public class TestEmpty1
        {
            [NonSerialized]
            public int BaseField;
            public TestEmpty1()
            {
                BaseField = 1;
            }
        }

        public sealed class TestEmpty2 : TestEmpty1
        {
            public TestEmpty2()
            {
            }
        }

        public sealed class Test8Args
        {
            public readonly int A;
            public readonly int B;
            public readonly string C;
            public readonly int D;
            public readonly int E;
            public readonly int F;
            public readonly int G;
            public readonly string H;

            public Test8Args(int a, int b, string c, int d, int e, int f, int g, string h)
            {
                A = a;
                B = b;
                D = d;
                E = e;
                C = c;
                F = f;
                G = g;
                H = h;
            }
        }

        public class PartialConstructorTest
        {
            public readonly int A;
            public readonly int B;
            public readonly int C;

            public PartialConstructorTest(int a)
            {
                A = a;
                B = 1;
                C = 2;
            }
        }

        public class TLoop
        {
            public BaseLoop x;

            public TLoop(BaseLoop x)
            {
                this.x = x;
            }
        }

        public class BaseLoop
        {
        }

        public class ConcreteLoop : BaseLoop
        {
            public int A;
            public int B;
            public TLoop? Inst;
            public string? Test;
        }

        [Fact]
        public void ConstructUsingEmpty()
        {
            var x = new Test {Value = 5};

            RoundTrip(x, (a, b) => { b.Value.Should().Be(0); });
        }

        [Fact]
        public void DoNotConstructUsingEmptyWithBaseClass()
        {
            var x = new TestEmpty2 { BaseField = 5 };

            RoundTrip(x, (a, b) => { b.BaseField.Should().Be(0); });
        }

        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        [Theory]
        public void DefaultCtorArguments(bool val1, bool val2)
        {
            var x = new Test2(val1, val2);

            RoundTrip(x);
        }

        [Fact]
        public void ConstructorWith8Args()
        {
            var x = new Test8Args(1, 2, "3", 4, 5, 6, 7, "8");

            RoundTrip(x);
        }

        [Fact]
        public void AnonymousTypeWith8Fields()
        {
            var x = new { a = 1, b = 2, c = 3, d = "d", e = 5, f = 6, g = "g", h = 8 };

            RoundTrip(x);
        }

        [Fact]
        public void PartialConstructor()
        {
            var x = new PartialConstructorTest(1);

            RoundTrip(x);
        }

        [Fact]
        public void LoopWithConstructor()
        {
            var y = new ConcreteLoop { A = 1, B = 2, Test = "asd" };
            var x = new TLoop(null!);
            x.x = y;
            y.Inst = x;

            RoundTripGraphOnly(x);
        }
    }
}
