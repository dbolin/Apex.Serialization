using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class LinkedListTests : AbstractSerializerTestBase
    {
        public static Type[] SerializableTypes()
        {
            return new[]
            {
                typeof(LinkedList<>)
            };
        }

        [Fact]
        public void LinkedListInt()
        {
            var x = new LinkedList<int>();
            x.AddLast(1);
            x.AddLast(2);
            x.AddLast(3);

            RoundTrip(x);
        }
    }
}
