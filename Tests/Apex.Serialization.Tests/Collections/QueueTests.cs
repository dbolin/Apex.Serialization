using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class QueueTests : AbstractSerializerTestBase
    {
        [Fact]
        public void QueueObject()
        {
            var x = new Queue<object?>();
            x.Enqueue(1);
            x.Enqueue("asd");
            x.Enqueue(null);

            RoundTrip(x);
        }
    }
}
