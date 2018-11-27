using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class LinkedListTests : AbstractSerializerTestBase
    {
        [Fact]
        public void LinkedListInt()
        {
            var x = new LinkedList<int>();
            x.AddLast(1);
            x.AddLast(2);
            x.AddLast(3);

            //TODO: should work with tree
            RoundTripGraphOnly(x);
        }
    }
}
