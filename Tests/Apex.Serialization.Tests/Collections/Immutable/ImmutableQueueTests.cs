using System.Collections.Immutable;
using Xunit;

namespace Apex.Serialization.Tests.Collections.Immutable
{
    public class ImmutableQueueTests : AbstractSerializerTestBase
    {
        [Fact]
        public void ImmutableQueueObject()
        {
            var x = ImmutableQueue<object?>.Empty;
            x = x.Enqueue(1);
            x = x.Enqueue("2");
            x = x.Enqueue(null);

            RoundTrip(x);
        }
    }
}
