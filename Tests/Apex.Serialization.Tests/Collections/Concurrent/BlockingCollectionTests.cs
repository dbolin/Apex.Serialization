using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Concurrent
{
    public class BlockingCollectionTests : AbstractSerializerTestBase
    {
        [Fact]
        public void BlockingCollectionObject()
        {
            var x = new BlockingCollection<object>();
            x.Add(1);
            x.Add("12");
            x.Add(null);
            x.Add(DateTime.UtcNow);

            RoundTrip(x);
        }
    }
}
