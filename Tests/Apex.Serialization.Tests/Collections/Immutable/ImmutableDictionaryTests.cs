using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Immutable
{
    public class ImmutableDictionaryTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ImmutableDictionaryObject()
        {
            var x = ImmutableDictionary<string, object>.Empty;
            x = x.Add("1", 1);
            x = x.Add("2", "2");
            x = x.Add("3", null);

            x = RoundTrip(x);

            x.ContainsKey("2").Should().Be(true);
        }
    }
}
