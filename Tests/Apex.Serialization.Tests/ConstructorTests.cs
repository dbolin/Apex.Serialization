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

        [Fact]
        public void ConstructUsingEmpty()
        {
            var x = new Test {Value = 5};

            RoundTrip(x, (a, b) => { b.Value.Should().Be(0); });
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
    }
}
