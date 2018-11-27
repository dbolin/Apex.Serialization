using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class SortedDictionaryTests : AbstractSerializerTestBase
    {
        [Fact]
        public void SortedDictionaryObject()
        {
            var x = new SortedDictionary<object, object>();
            x.Add("", "A");
            x.Add("A", "A");
            x.Add("B", null);

            RoundTrip(x);
        }
    }
}
