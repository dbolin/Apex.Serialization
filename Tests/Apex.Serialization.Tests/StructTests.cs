using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class StructTests : AbstractSerializerTestBase
    {
        [StructLayout(LayoutKind.Explicit)]
        public struct Value
        {
            [FieldOffset(0)] public int IntValue;
            [FieldOffset(0)] public decimal DecimalValue;
        }

        public struct TestEmpty
        {
        }

        public struct TestSingle
        {
            public long Value;
        }

        public struct TestNested
        {
            public TestEmpty A1;
            public TestSingle A2;
            public TestSingle A3;
            public TestSingle A4;
            public TestSingle A5;
            public TestSingle A6;
            public TestSingle A7;
            public TestSingle A8;
            public TestSingle A9;
            public TestSingle A10;
            public TestEmpty A11;
        }

        [Fact]
        public void ExplicitLayoutInt()
        {
            var x = new Value { IntValue = 4 };

            RoundTrip(x);
        }

        [Fact]
        public void ExplicitLayoutDecimal()
        {
            var x = new Value { DecimalValue = 1.2m };

            RoundTrip(x);
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct ExplicitReferences
        {
            [FieldOffset(0)]
            public string StringValue;
            [FieldOffset(0)]
            public object ObjectValue;
        }

        [Fact]
        public void ExplicitLayoutReferences()
        {
            var x = new ExplicitReferences { ObjectValue = 12 };

            RoundTrip(x);

            x = new ExplicitReferences { StringValue = "abc" };

            RoundTrip(x);
        }

        [Fact]
        public void Empty()
        {
            var x = new TestEmpty();

            RoundTrip(x);
        }

        [Fact]
        public void Single()
        {
            var x = new TestSingle { Value = 1 };

            RoundTrip(x);
        }

        [Fact]
        public void Nested()
        {
            var x = new TestNested { A4 = new TestSingle { Value = 4 } };

            RoundTrip(x);
        }
    }
}
