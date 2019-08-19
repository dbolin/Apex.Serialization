using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Concurrent
{
    public class ConcurrentBagTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ConcurrentBagObject()
        {
            var x = new ConcurrentBag<int> {1, 2, 3};

            RoundTrip(x);
        }
    }
}
