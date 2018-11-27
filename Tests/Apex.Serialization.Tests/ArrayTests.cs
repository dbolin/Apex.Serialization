using System;
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
        }

        [Fact]
        public void Arrays()
        {
            var x = new Test
            {
                IntArray = new int[] { 1, 2, 3, 4 },
                StringArray = new string[] { "asd", "qwe" },
                StructArray = new Test2[] { new Test2 { Value = 4 } },
                RefArray = new Test3[] { new Test3 { Value = 5 } },
                ObjectArray = new object[] { 1, "asd", new Test2 { Value = 6 }, new Test3 { Value = 7 }, null, new object[] { 1, "zxc" } },
                ArrayOfArrays = new int[][] { new int[] { 1, 2, 3 }, new int[] { 4, 5, 6, 7, 8 } },
                MultiDimensionalArray = new int[,] { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } }
            };

            RoundTrip(x);
        }

        [Fact]
        public void NullArrays()
        {
            var x = new Test();

            RoundTrip(x);
        }
    }
}
