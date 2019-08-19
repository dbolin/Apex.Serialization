using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Immutable
{
    public class ImmutableArrayTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ImmutableArrayObject()
        {
            var x = ImmutableArray<object?>.Empty;
            x = x.Add(1);
            x = x.Add("2");
            x = x.Add(null);

            RoundTrip(x);
        }

        public class Container
        {
            public string A { get; }
            public ImmutableArray<Guid> B { get; }
            public string C { get; }

            public Container(string a, ImmutableArray<Guid> b, string c)
            {
                A = a;
                B = b;
                C = c;
            }
        }

        [Fact]
        public void ObjectContainingImmutableArray()
        {
            var c = new Container("A", ImmutableArray<Guid>.Empty, "C");
            var x = new List<Container>
            {
                c,
                c,
                c
            };

            RoundTrip(x);
        }
    }
}
