using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests.Collections
{
    public class ListTests : AbstractSerializerTestBase
    {
        public static Type[] SerializableTypes()
        {
            return new[]
            {
                typeof(List<>)
            };
        }

        public sealed class Test
        {
            public int Value;
            public Guid g;
            public DateTime d;
        }

        public struct Test2
        {
            public int Value2;
            public string Value3;
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
            var x = new List<Test> {new Test {Value = 2, d = DateTime.UtcNow, g = Guid.NewGuid()}};

            RoundTrip(x);
        }

        [Fact]
        public void ListOfSealedTypeGraph()
        {
            var t = new Test {Value = 2};
            var x = new List<Test> {t, t};

            var y = RoundTripGraphOnly(x);

            y[0].GetHashCode().Should().Be(y[1].GetHashCode());
        }

        [Fact]
        public void ListOfStruct()
        {
            var x = new List<Test2> { new Test2 { Value2 = 2, Value3 = "asd"} };

            RoundTrip(x);
        }

        [Fact]
        public void ListOfObject()
        {
            var x = new List<object?> {null, "asd", 1, new Test {Value = 3}, Guid.NewGuid()};

            RoundTrip(x);
        }
    }
}
