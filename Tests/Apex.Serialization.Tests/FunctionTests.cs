using System;
using System.Collections.Generic;
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
            public Func<int>? A;
            public Func<int>? B;
            public Func<int>? C;
            public Func<int>? D;
        }

        public class Test2
        {
            public object? A;
            public object? B;
            public object? C;
            public object? D;
        }

#pragma warning disable CS8653 // A default expression introduces a null value for a type parameter.
        public class Test3<T>
        {
            public static T F1(T value)
            {
                return default!;
            }

            public T F2(T value)
            {
                return default!;
            }

            public T2 F3<T2>(T value)
                where T2 : struct
            {
                return default;
            }

            public void F4<T4>()
            {
            }
        }
#pragma warning restore CS8653 // A default expression introduces a null value for a type parameter.

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

            ((Func<int>)y.A!)().Should().Be(3);
            ((Func<int>)y.B!)().Should().Be(4);
            ((Func<int>)y.C!)().Should().Be(5);
            ((Func<int>)y.D!)().Should().Be(6);
        }

        [Fact]
        public void GenericFunctions()
        {
            var t = new Test3<Test3<int>>();
            var x = new Test2
            {
                A = (Func<Dictionary<string, Test3<int>>, Dictionary<string, Test3<int>>>)
                    Test3<Dictionary<string, Test3<int>>>.F1,
                B = (Func<Test3<int>, Test3<int>>) t.F2,
                C = (Func<Test3<int>, bool>) t.F3<bool>,
                D = (Action) t.F4<decimal>
            };

            RoundTrip(x, (a, b) => true);
        }
    }
}
