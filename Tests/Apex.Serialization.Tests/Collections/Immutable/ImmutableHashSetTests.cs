using Apex.Serialization.Tests.Collections.Objects;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Immutable
{
    public class ImmutableHashSetTests : AbstractSerializerTestBase
    {
        public static Type[] SerializableTypes()
        {
            return new[]
            {
                typeof(ImmutableHashSet<>),
                typeof(ImmutableList<>)
            };
        }

        [Fact]
        public void ImmutableHashSetObject()
        {
            var x = ImmutableHashSet<object?>.Empty;
            x = x.Add(1);
            x = x.Add("2");
            x = x.Add(null);

            RoundTrip(x);
        }

        /*
        public void RandomHashcodes()
        {
            var element = new RandomHashcode { Value = 10 };
            var x = ImmutableHashSet<RandomHashcode>.Empty;

            x = x.Add(element);

            RandomHashcode.NewRandomizer();

            var y = RoundTrip(x, (a, b) => true);

            y.First().GetHashCode().Should().Be(element.GetHashCode());

            y.Contains(element).Should().Be(true);
        }
        */
    }
}
