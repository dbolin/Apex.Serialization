using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class EnumeratorTests : AbstractSerializerTestBase
    {
        public static Type[] SerializableTypes()
        {
            return new[]
            {
                typeof(Dictionary<,>)
            };
        }

        [Fact]
        public void DictionaryEnumerator()
        {
            var d = new Dictionary<string, int>();
            d.Add("1", 1);
            d.Add("2", 2);
            d.Add("3", 3);

            var copy = new Dictionary<string, int>();

            var e = d.GetEnumerator();

            e.MoveNext();
            copy.Add(e.Current.Key, e.Current.Value);

            e = RoundTrip(e, (a,b) => true);

            e.MoveNext();
            copy.Add(e.Current.Key, e.Current.Value);
            e.MoveNext();
            copy.Add(e.Current.Key, e.Current.Value);

            copy.Should().BeEquivalentTo(d);
        }
    }
}
