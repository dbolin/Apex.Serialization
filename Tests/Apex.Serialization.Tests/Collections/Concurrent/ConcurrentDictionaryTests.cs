using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Concurrent
{
    public class ConcurrentDictionaryTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ConcurrentDictionaryObject()
        {
            var x = new ConcurrentDictionary<int, object>();
            x.TryAdd(1, 1);
            x.TryAdd(2, "@");
            x.TryAdd(3, null);

            RoundTrip(x);
        }
    }
}
