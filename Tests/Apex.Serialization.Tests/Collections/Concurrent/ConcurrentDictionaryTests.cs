using Apex.Serialization.Tests.Collections.Objects;
using FluentAssertions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Concurrent
{
    public class ConcurrentDictionaryTests : AbstractSerializerTestBase
    {
        public static Type[] SerializableTypes()
        {
            return new[]
            {
                typeof(ConcurrentDictionary<,>)
            };
        }

        [Fact]
        public void ConcurrentDictionaryObject()
        {
            var x = new ConcurrentDictionary<int, object?>();
            x.TryAdd(1, 1);
            x.TryAdd(2, "@");
            x.TryAdd(3, null);

            RoundTrip(x);
        }

        [Fact]
        public void RandomHashcodesNotSupported()
        {
            var element = new RandomHashcode {Value = 10};
            var x = new ConcurrentDictionary<RandomHashcode, int>();
            x.TryAdd(element, 1);

            RandomHashcode.NewRandomizer();

            var y = RoundTrip(x, (a, b) => true);

            y.First().Key.GetHashCode().Should().Be(element.GetHashCode());

            y.ContainsKey(element).Should().Be(false);
            //y[element].Should().Be(1);
        }
    }
}
