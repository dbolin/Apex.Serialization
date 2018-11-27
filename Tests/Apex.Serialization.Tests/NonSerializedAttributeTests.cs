using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class NonSerializedAttributeTests : AbstractSerializerTestBase
    {
        public class Test
        {
            public int Value;
            [NonSerialized]
            public int Value2;
        }

        [Fact]
        public void FieldAttribute()
        {
            var x = new Test
            {
                Value = 3,
                Value2 = 2
            };

            RoundTrip(x, (a, b) =>
            {
                a.Value.Should().Be(b.Value);
                b.Value2.Should().Be(0);
            });
        }
    }
}
