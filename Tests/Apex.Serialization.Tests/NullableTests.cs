using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class NullableTests : AbstractSerializerTestBase
    {
        public struct Test2
        {
            public int Value;
        }

        public class Test1
        {
            public int? Value1;
            public decimal? Value2;
            public DateTime? Value3;
            public Test2? Value4;
        }

        [Fact]
        public void NullableFieldsNotNull()
        {
            var x = new Test1
            {
                Value1 = 1,
                Value2 = 2.0m,
                Value3 = DateTime.UtcNow,
                Value4 = new Test2()
            };

            RoundTrip(x);
        }

        [Fact]
        public void NullableFieldsNull()
        {
            var x = new Test1();

            RoundTrip(x);
        }
    }
}
