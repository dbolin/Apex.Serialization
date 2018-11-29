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

        [Fact]
        public void ConstructUsingEmpty()
        {
            var x = new Test {Value = 5};

            RoundTrip(x, (a, b) => { b.Value.Should().Be(0); });
        }
    }
}
