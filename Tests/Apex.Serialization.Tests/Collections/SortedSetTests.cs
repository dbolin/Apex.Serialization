using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class SortedSetTests : AbstractSerializerTestBase
    {
        public static Type[] SerializableTypes()
        {
            return new[]
            {
                typeof(SortedSet<>),
            };
        }

        [Fact]
        public void SortedSetObject()
        {
            var x = new SortedSet<string> {"1", "23", "13"};

            RoundTrip(x);
        }
    }
}
