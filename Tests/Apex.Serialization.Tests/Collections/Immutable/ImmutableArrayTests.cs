using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Immutable
{
    public class ImmutableArrayTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ImmutableArrayObject()
        {
            var x = ImmutableArray<object>.Empty;
            x = x.Add(1);
            x = x.Add("2");
            x = x.Add(null);

            RoundTrip(x);
        }
    }
}
