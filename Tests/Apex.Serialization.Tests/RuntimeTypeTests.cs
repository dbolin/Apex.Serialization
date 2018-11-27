using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class RuntimeTypeTests : AbstractSerializerTestBase
    {
        public class Test
        {
            public Type A;
            public Type B;
            public object C;
        }

        [Fact]
        public void NonGenericType()
        {
            var x = typeof(RuntimeTypeTests);

            RoundTrip(x);
        }

        [Fact]
        public void TypeInObjectField()
        {
            var x = new Test { C = typeof(RuntimeTypeTests) };

            RoundTrip(x);
        }

        [Fact]
        public void MultipleSameTypes()
        {
            var x = new Test
            {
                A = typeof(int),
                B = typeof(int),
                C = typeof(int)
            };

            RoundTrip(x);
        }

        [Fact]
        public void ClosedGeneric()
        {
            var x = typeof(List<int>);

            RoundTrip(x);
        }

        [Fact]
        public void OpenGeneric()
        {
            var x = typeof(Dictionary<,>);

            RoundTrip(x);
        }
    }
}
