using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Concurrent
{
    public class ConcurrentQueueTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ConcurrentQueueObject()
        {
            var x = new ConcurrentQueue<object?>();
            x.Enqueue(1);
            x.Enqueue("asd");
            x.Enqueue(null);

            RoundTrip(x);
        }
    }
}
