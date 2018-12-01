using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Apex.Serialization.Tests.Collections.Objects;
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

        public void RandomHashcodes()
        {
            var element = new RandomHashcode { Value = 10 };
            var x = ImmutableDictionary<RandomHashcode, int>.Empty;

            x = x.Add(element, 1);

            RandomHashcode.NewRandomizer();

            var y = RoundTrip(x, (a, b) => true);

            y.First().Key.GetHashCode().Should().Be(element.GetHashCode());

            y.ContainsKey(element).Should().Be(true);
            y[element].Should().Be(1);
        }
    }
}
