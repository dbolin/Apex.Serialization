using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class AnonymousTypeTests : AbstractSerializerTestBase
    {
        [Fact]
        public void AnonymousType()
        {
            var x = new {prop1 = 1, prop2 = "asd", prop3 = new {nestedProp = 1}};

            RoundTrip(x);
        }
    }
}
