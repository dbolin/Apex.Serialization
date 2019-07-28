using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class StringTests : AbstractSerializerTestBase
    {
        public class Test
        {
            public string? Value;
            public object? MightBeAString;
        }

        [Fact]
        public void StringInStringField()
        {
            var x = new Test
            {
                Value = "asd"
            };

            RoundTrip(x);
        }

        [Fact]
        public void StringInObjectField()
        {
            var x = new Test
            {
                Value = "qwe",
                MightBeAString = "qwerty"
            };

            RoundTrip(x);
        }

        [Fact]
        public void NullString()
        {
            var x = new Test();

            RoundTrip(x);
        }
    }
}
