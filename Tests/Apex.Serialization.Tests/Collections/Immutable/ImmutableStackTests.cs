using System;
using System.Collections.Immutable;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Immutable
{
    public class ImmutableStackTests : AbstractSerializerTestBase
    {
        public static Type[] SerializableTypes()
        {
            return new[]
            {
                typeof(ImmutableStack<>)
            };
        }

        [Fact]
        public void ImmutableStackObject()
        {
            var x = ImmutableStack<object?>.Empty;
            x = x.Push(1);
            x = x.Push("2");
            x = x.Push(null);

            x = RoundTrip(x);

            x.Peek().Should().Be(null);
        }
    }
}
