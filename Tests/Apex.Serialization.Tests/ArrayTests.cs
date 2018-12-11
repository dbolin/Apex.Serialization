﻿using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class ArrayTests : AbstractSerializerTestBase
    {
        public struct Test2
        {
            public int Value;
        }

        public class Test3
        {
            public int Value;
        }

        public class Test
        {
            public int[] IntArray;
            public string[] StringArray;
            public Test2[] StructArray;
            public Test3[] RefArray;
            public object[] ObjectArray;

            public int[][] ArrayOfArrays;
            public int[,] MultiDimensionalArray;
            public DateTime[] DateTimeArray;
            public Guid[] GuidArray;
            public decimal?[] NullableDecimalArray;
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
            var x = new object[] {1, "asd", o, o, new Test2 {Value = 7}, null, new object[] {1, "zxc"}};

            RoundTrip(x);
        }

        [Fact]
        public void MultiDimensionalArray()
        {
            var x = new int[,] {{1, 2, 3}, {4, 5, 6}, {7, 8, 9}};

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
            var x = new[] {12.0m, (decimal?) null};

            RoundTrip(x);
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
    }
}
