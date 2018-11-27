using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class ReadonlyTests : AbstractSerializerTestBase
    {
        public struct TestStruct
        {
            public int Value1;
            public int Value2;
        }

        public class Test1
        {
            public readonly int PublicField;
            private readonly int privateField;
            public int PublicProperty { get; }
            private int privateProperty { get; }

            private readonly TestStruct privateStruct;

            public Test1(int a, int b, int c, int d, TestStruct test)
            {
                PublicField = a;
                privateField = b;
                PublicProperty = c;
                privateProperty = d;
                privateStruct = test;
            }

            public (int, int, int, int, TestStruct) GetValues()
            {
                return (PublicField, privateField, PublicProperty, privateProperty, privateStruct);
            }
        }

        [Fact]
        public void PrivateFieldsAndProperties()
        {
            var x = new Test1(1, 2, 3, 4, new TestStruct { Value1 = 6, Value2 = 7 });

            RoundTrip(x, (a, b) => a.GetValues().Should().BeEquivalentTo(b.GetValues()));
        }
    }
}
