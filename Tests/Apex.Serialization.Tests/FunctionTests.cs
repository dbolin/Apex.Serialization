using System;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class FunctionTests : AbstractSerializerTestBase
    {
        public class TestClass
        {
            private static int StaticValue = 4;
            private int Value = 3;

            public int Method()
            {
                return Value;
            }

            public static int StaticMethod()
            {
                return StaticValue;
            }

            public static int StaticMethod(int param)
            {
                return StaticValue;
            }
        }

        public class Test
        {
            public Func<int> A;
            public Func<int> B;
            public Func<int> C;
            public Func<int> D;
        }

        public class Test2
        {
            public object A;
            public object B;
            public object C;
            public object D;
        }

        [Fact]
        public void Functions()
        {
            var testClass = new TestClass();
            var closureInt = 6;
            var x = new Test
            {
                A = testClass.Method,
                B = TestClass.StaticMethod,
                C = () => 5,
                D = () => closureInt
            };

            var y = RoundTrip(x, (a,b) => true);

            y.A().Should().Be(3);
            y.B().Should().Be(4);
            y.C().Should().Be(5);
            y.D().Should().Be(6);
        }

        [Fact]
        public void FunctionsInObjectFields()
        {
            var testClass = new TestClass();
            var closureInt = 6;
            var x = new Test2
            {
                A = (Func<int>)testClass.Method,
                B = (Func<int>)TestClass.StaticMethod,
                C = (Func<int>)(() => 5),
                D = (Func<int>)(() => closureInt)
            };

            var y = RoundTrip(x, (a, b) => true);

            ((Func<int>)y.A)().Should().Be(3);
            ((Func<int>)y.B)().Should().Be(4);
            ((Func<int>)y.C)().Should().Be(5);
            ((Func<int>)y.D)().Should().Be(6);
        }
    }
}
