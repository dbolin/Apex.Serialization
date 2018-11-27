using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Immutable
{
    public class ImmutableSortedSetTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ImmutableSortedSetObject()
        {
            var x = ImmutableSortedSet<string>.Empty;
            x = x.Add("1");
            x = x.Add("2");
            x = x.Add("3");

            x = RoundTrip(x);
        }
    }
}
