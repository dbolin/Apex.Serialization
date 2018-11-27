using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class HashSetTests : AbstractSerializerTestBase
    {
        [Fact]
        public void HashSetInt()
        {
            var x = new HashSet<int> {1, 2, 3};

            RoundTrip(x);
        }

        [Fact]
        public void HashSetString()
        {
            var x = new HashSet<string> { "a", "aa", "aaa", "abc" };

            var y = RoundTrip(x);

            y.Contains("a").Should().Be(true);
            y.Contains("aa").Should().Be(true);
            y.Contains("aaa").Should().Be(true);
            y.Contains("abc").Should().Be(true);
        }
    }
}
