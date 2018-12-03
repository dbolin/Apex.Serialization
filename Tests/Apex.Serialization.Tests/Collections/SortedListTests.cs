using System.Collections.Generic;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class SortedListTests : AbstractSerializerTestBase
    {
        [Fact]
        public void SortedListObject()
        {
            var x = new SortedList<int, object> {{1, 1}, {2, "2"}, {3, null}};

            RoundTrip(x);
        }
    }
}
