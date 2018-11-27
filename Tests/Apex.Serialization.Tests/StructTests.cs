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

        [Fact]
        public void ExplicitLayoutInt()
        {
            var x = new Value {IntValue = 4};

            RoundTrip(x);
        }

        [Fact]
        public void ExplicitLayoutDecimal()
        {
            var x = new Value { DecimalValue = 1.2m };

            RoundTrip(x);
        }
    }
}
