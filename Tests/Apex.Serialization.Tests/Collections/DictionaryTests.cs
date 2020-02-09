using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apex.Serialization.Tests.Collections.Objects;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class DictionaryTests : AbstractSerializerTestBase
    {
        public static Type[] SerializableTypes()
        {
            return new[]
            {
                typeof(Dictionary<,>)
            };
        }

        public sealed class Test
        {
            public int Value;
        }

        public class Test2
        {
            public Dictionary<string, string>? Values;
        }

        public class InheritedDictionary : Dictionary<string, object>
        {
            public int OwnField;

            public new int Count => 123;

            public new IEnumerator<int> GetEnumerator()
            {
                throw new NotSupportedException();
            }
        }

        [Fact]
        public void IntDictionary()
        {
            var x = new Dictionary<int, int> {{1, 1}, {2, 2}};

            RoundTrip(x, s => s.SerializationMode == Mode.Graph);
        }

        [Fact]
        public void StringDictionary()
        {
            var x = new Dictionary<string, string> {{"a", "a"}, {"b", "b"}};

            x = RoundTrip(x);

            x["a"].Should().Be("a");
            x["b"].Should().Be("b");
        }

        [Fact]
        public void ObjectDictionary()
        {
            var x = new Dictionary<object, object> {{1, 1}, {"a", "a"}, {new Test {Value = 3}, new Test {Value = 4}}};

            RoundTrip(x, (a, b) =>
            {
                a.Keys.Should().BeEquivalentTo(b.Keys);
                a.Values.Should().BeEquivalentTo(b.Values);
            });
        }

        [Fact]
        public void NullDictionaryMember()
        {
            var x = new Test2();

            RoundTrip(x);
        }

        [Fact]
        public void DictionaryMember()
        {
            var x = new Test2
                {Values = new Dictionary<string, string> {{"a", "a"}, {"b", "b"}, {"c", "c"}, {"d", "d"}}};

            RoundTrip(x);
        }

        [Fact]
        public void CustomDictionary()
        {
            var x = new InheritedDictionary {OwnField = 4};
            x.Add("test", "test2");

            x = RoundTrip(x, s => s.SerializationMode == Mode.Graph);

            x.ContainsKey("test").Should().Be(true);
            x.OwnField.Should().Be(4);
        }

        [Fact]
        public void RandomHashcodesNotSupported()
        {
            var element = new RandomHashcode {Value = 10};
            var x = new Dictionary<RandomHashcode, int> {{element, 1}};

            RandomHashcode.NewRandomizer();

            var y = RoundTrip(x, (a,b) => true);

            y.First().Key.GetHashCode().Should().Be(element.GetHashCode());

            y.ContainsKey(element).Should().Be(false);
            //y[element].Should().Be(1);
        }

        [Fact]
        public void ClassAsKey()
        {
            var x = new Dictionary<Test2, int>();
            x.Add(new Test2(), 1);

            RoundTrip(x, (a,b) => b.First().Value.Should().Be(1));
        }
    }
}
