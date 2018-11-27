using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Immutable
{
    public class ImmutableHashSetTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ImmutableHashSetObject()
        {
            var x = ImmutableHashSet<object>.Empty;
            x = x.Add(1);
            x = x.Add("2");
            x = x.Add(null);

            RoundTrip(x);
        }
    }
}
