using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class ArrayTests : AbstractSerializerTestBase
    {
        public static Type[] SerializableTypes()
        {
            return new[]
            {
                typeof(ImmutableArray<>),
                typeof(CustomProperty),
                typeof(Value),
                typeof(PrimitiveValue)
            };
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct ExplicitLayoutTest2
        {
            [FieldOffset(0)]
            public int Value;
            [FieldOffset(0)]
            public int Value2;
        }

        public struct Test2
        {
            public int Value;
        }

        public class Test3
        {
            public int Value;
        }

        public struct Test4
        {
            public Test3 Value;
        }

        public sealed class Test5
        {
            public int Value1;
            public int Value2;
            public int Value3;
            public Test4 Test4;
        }

        public class Test
        {
            public int[]? IntArray;
            public string[]? StringArray;
            public Test2[]? StructArray;
            public Test3[]? RefArray;
            public object[]? ObjectArray;

            public int[][]? ArrayOfArrays;
            public int[,]? MultiDimensionalArray;
            public DateTime[]? DateTimeArray;
            public Guid[]? GuidArray;
            public decimal?[]? NullableDecimalArray;
        }

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

        [Fact]
        public void IntArray()
        {
            var x = new[] {1, 2, 3, 4};

            RoundTrip(x);
        }

        [Fact]
        public void ManyIntArray()
        {
            var a = new int[0];
            var x = new[] { new[] { 1,2,3,4,5,6,7 }, a, a, a, null, new[] { 4 } };

            RoundTrip(x);
        }

        [Fact]
        public void BlittableArrays()
        {
            RoundTrip(new byte[] { 0, 1 });
            RoundTrip(new sbyte[] { 0, -1 });
            RoundTrip(new short[] { 0, -1 });
            RoundTrip(new ushort[] { 0, 1 });
            RoundTrip(new uint[] { 0, 1 });
            RoundTrip(new long[] { 0, -1 });
            RoundTrip(new ulong[] { 0, 1 });
            RoundTrip(new float[] { 0, 1 });
            RoundTrip(new double[] { 0, 1 });
            RoundTrip(new char[] { 'a', 'b' });
            RoundTrip(new decimal[] { 12.1m, 0.00m });
            RoundTrip(new bool[] { false, true });
        }

        [Fact]
        public void StringArray()
        {
            var x = new[] {"asd", null, "qwe"};

            RoundTrip(x);
        }

        [Fact]
        public void StructArray()
        {
            var x = new[] {new Test2 {Value = 4}};

            RoundTrip(x);

            var y = new[] { new ExplicitLayoutTest2 { Value = 5 } };

            var y2 = RoundTrip(y);

            y2[0].Value2.Should().Be(5);
        }

        [Fact]
        public void NullableStructArray()
        {
            var x = new Test2?[] { new Test2 { Value = 1 }, null, null, null, null, new Test2 { Value = 2 } };

            RoundTrip(x);
        }

        [Fact]
        public void RefArray()
        {
            var x = new[] {new Test3 {Value = 5}};

            RoundTrip(x);
        }

        [Fact]
        public void ObjectArray()
        {
            var o = new Test3 {Value = 6};
            var x = new object?[] {1, "asd", o, o, new Test2 {Value = 7}, null, new object[] {1, "zxc"}};

            RoundTrip(x);
        }

        [Fact]
        public void MultiDimensionalArrayInt()
        {
            var x = new int[,] {{1, 2, 3}, {4, 5, 6}, {7, 8, 9}};

            RoundTrip(x);
        }

        [Fact]
        public void MultiDimensionalArrayInt3()
        {
            var x = new int[,,] { { { 1 }, { 2 }, { 3 } }, { { 4 }, { 5 }, { 6 } }, { { 7 }, { 8 }, { 9 } } };

            RoundTrip(x);
        }

        [Fact]
        public void MultiDimensionalArrayStruct()
        {
            var v = new Vector3(1, 2, 3);
            var x = new Vector3[,] { { v, v*2, v*3 }, { v*4, v*5, v*6 }, { v*7, v*8, v*9 } };

            RoundTrip(x);
        }

        [Fact]
        public void DateTimeArray()
        {
            var x = new[] {DateTime.UtcNow};

            RoundTrip(x);
        }

        [Fact]
        public void GuidArray()
        {
            var x = new[] {Guid.NewGuid()};

            RoundTrip(x);
        }

        [Fact]
        public void NullableDecimalArray()
        {
            var x = new[] {12.0m, (decimal?) null, 13m, 14m, 15m, 16m, 17m, null, 18m};

            RoundTrip(x);
        }

        public sealed class TestObject1
        {
            public decimal O { get; }
            public string? Im { get; }
            public string? T { get; }
            public string? In { get; }
            public ImmutableArray<CustomProperty>? Cu { get; }

            public TestObject1(
                ImmutableArray<CustomProperty>? cu)
            {
                Cu = cu;
            }
        }

        [Fact]
        public void NestedNullable()
        {
            var a = Enumerable.Range(0, 100).Select(x => new CustomProperty("", new Value { _string = "" })).ToImmutableArray();
            var x = new TestObject1(a);

            RoundTrip(x, (a,b) => true);
        }

        [Fact]
        public void NullArrays()
        {
            var x = new Test();

            RoundTrip(x);
        }

        [Fact]
        public void ArrayOfType()
        {
            var x = new[] {typeof(int), typeof(string)};

            RoundTrip(x);
        }

        [Fact]
        public void ArrayOfDelegate()
        {
            var x = new Func<int>[] {TestClass.StaticMethod};

            RoundTrip(x, (a,b) => true);
        }

        [Fact]
        public void ArrayOfArray()
        {
            var x = new int[][] {new int[] {1, 2, 3, 4}, new[] {4, 5, 6, 7}};

            RoundTrip(x);
        }

        [Fact]
        public void ArrayOfSealedContainingStructWithSingleRefField()
        {
            var x = new[] { new Test5 { Test4 = new Test4 { Value = new Test3() { Value = 1 } } } };

            RoundTrip(x, (a,b) => a[0].Test4.Value.Value == b[0].Test4.Value.Value);
        }

        [Fact]
        public void ArrayOfManyNullsAndThenValue()
        {
            var x = Enumerable.Repeat((Test5?)null, 1000).Concat(new Test5[] { new Test5 { Value1 = 1} }).ToArray();

            RoundTrip(x);
        }

        public class OuterWrapper
        {
            public OuterWrapper[]? ArrayOuter;
            public OuterWrapper? Self;

            public InnerWrapper[]? ArrayInner;
        }

        public class InnerWrapper
        {
            public OuterWrapper? Ref;
            public OuterWrapper[]? Array;
        }

        [Fact]
        public void InliningSelfReferences()
        {
            var x = new OuterWrapper();
            x.Self = x;
            x.ArrayOuter = new[] { x };
            x.ArrayInner = new[] { new InnerWrapper { Ref = x, Array = new[] { x } } };

            RoundTripGraphOnly(x, (x,y) =>
            {
                y.Self.Should().Be(y);
                y.ArrayOuter[0].Should().Be(y);
                y.ArrayInner[0].Ref.Should().Be(y);
                y.ArrayInner[0].Array[0].Should().Be(y);
            });
        }
    }
}
