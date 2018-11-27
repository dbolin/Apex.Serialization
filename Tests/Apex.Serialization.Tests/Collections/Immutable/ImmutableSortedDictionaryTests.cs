using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Immutable
{
    public class ImmutableSortedDictionaryTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ImmutableSortedDictionaryObject()
        {
            var x = ImmutableSortedDictionary<string, object>.Empty;
            x = x.Add("1", 1);
            x = x.Add("2", "2");
            x = x.Add("3", null);

            x = RoundTrip(x);

            x.ContainsKey("2").Should().Be(true);
        }
    }
}
