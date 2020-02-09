using Apex.Serialization.Internal;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class InternTests : AbstractSerializerTestBase
    {
        public static readonly object TheObject = new object();

        [Fact]
        public void InternedObjectShouldKeepReferenceEquality()
        {
            _setupSerializerGraph = s => (s as IBinary).Intern(TheObject);
            RoundTrip(TheObject, (original, loaded) => ReferenceEquals(loaded, TheObject).Should().BeTrue(), s => s.SerializationMode == Mode.Graph);
        }
    }
}
