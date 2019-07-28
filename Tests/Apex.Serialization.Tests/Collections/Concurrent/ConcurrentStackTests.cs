using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Concurrent
{
    public class ConcurrentStackTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ConcurrentStackObject()
        {
            var x = new ConcurrentStack<object?>();
            x.Push(1);
            x.Push("");
            x.Push(null);

            x = RoundTrip(x);

            x.TryPeek(out var res);
            res.Should().BeNull();
        }
    }
}
