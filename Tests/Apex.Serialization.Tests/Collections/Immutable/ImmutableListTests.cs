using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Immutable
{
    public class ImmutableListTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ImmutableListObject()
        {
            var x = ImmutableList<object>.Empty;
            x = x.Add(1);
            x = x.Add("2");
            x = x.Add(null);

            RoundTrip(x);
        }
    }
}
