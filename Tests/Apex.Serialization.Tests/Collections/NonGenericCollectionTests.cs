using System.Collections;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class NonGenericCollectionTests : AbstractSerializerTestBase
    {
        public class Test
        {
            public int Value;
        }

        [Fact]
        public void ArrayList()
        {
            var x = new ArrayList {1, 2, "asd", null, new Test {Value = 3}};

            RoundTrip(x);
        }

        [Fact]
        public void BitArray()
        {
            var x = new BitArray(16);
            x.Set(1, true);

            RoundTrip(x);
        }

        /*
        public void Hashtable()
        {
            var x = new Hashtable {{1, 1}, {"asd", "Asd"}, {3, null}, {new Test {Value = 5}, new Test {Value = 6}}};

            x = RoundTrip(x, (a,b) =>
            {
                a.Keys.Should().BeEquivalentTo(b.Keys);
                a.Values.Should().BeEquivalentTo(b.Values);
            });

            x[1].Should().Be(1);
            x["asd"].Should().Be("Asd");
        }
        */

        [Fact]
        public void Queue()
        {
            var x = new Queue();
            x.Enqueue(1);
            x.Enqueue(null);
            x.Enqueue("asd");
            x.Enqueue(new Test {Value = 3});

            RoundTrip(x);
        }

        /*
        public void SortedList()
        {
            var x = new SortedList { { "aaa", 1 }, { "asd", "Asd" }, { "z", null }, { "x", new Test { Value = 6 } } };

            x = RoundTrip(x);

            x["aaa"].Should().Be(1);
            x["asd"].Should().Be("Asd");
        }
        */

        [Fact]
        public void Stack()
        {
            var x = new Stack();
            x.Push(1);
            x.Push(null);
            x.Push("asd");
            x.Push(new Test { Value = 3 });

            RoundTrip(x);
        }

    }
}
