using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class ListTests : AbstractSerializerTestBase
    {
        public sealed class Test
        {
            public int Value;
        }

        [Fact]
        public void ListOfInt()
        {
            var x = new List<int> {1, 2, 3};

            RoundTrip(x);
        }

        [Fact]
        public void ListOfString()
        {
            var x = new List<string> {"asd", "qwe"};

            RoundTrip(x);
        }

        [Fact]
        public void ListOfSealedType()
        {
            var x = new List<Test> {new Test {Value = 2}};

            RoundTrip(x);
        }

        [Fact]
        public void ListOfObject()
        {
            var x = new List<object> {null, "asd", 1, new Test {Value = 3}, Guid.NewGuid()};

            RoundTrip(x);
        }
    }
}
