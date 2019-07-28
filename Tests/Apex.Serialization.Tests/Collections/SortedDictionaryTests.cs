using Apex.Serialization.Tests.Collections.Objects;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class SortedDictionaryTests : AbstractSerializerTestBase
    {
        [Fact]
        public void SortedDictionaryObject()
        {
            var x = new SortedDictionary<object, object?>();
            x.Add("", "A");
            x.Add("A", "A");
            x.Add("B", null);

            RoundTrip(x);
        }

        [Fact]
        public void RandomHashcodes()
        {
            var element = new RandomHashcode { Value = 10 };
            var x = new SortedDictionary<RandomHashcode, int> { {element, 1} };

            RandomHashcode.NewRandomizer();

            var y = RoundTrip(x, (a, b) => true);

            y.First().Key.GetHashCode().Should().Be(element.GetHashCode());

            y.ContainsKey(element).Should().Be(true);
            y[element].Should().Be(1);
        }
    }
}
