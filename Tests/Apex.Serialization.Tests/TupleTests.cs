using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class TupleTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ValueTuples()
        {
            var x = (1, "asd");

            RoundTrip(x);
        }

        [Fact]
        public void Tuples()
        {
            var x = Tuple.Create(1, "asd");

            RoundTrip(x);
        }
    }
}
