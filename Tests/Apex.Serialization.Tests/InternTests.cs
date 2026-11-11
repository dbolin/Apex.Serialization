using System.IO;
using System.Linq;
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

        public sealed class Marker
        {
        }

        public sealed class Node
        {
            public object? First;
            public object? Second;
            public Node? Next;
        }

        [Fact]
        public void ASerializerWritesAndReadsInternedObjectsTheSameWhateverItHandledBefore()
        {
            var interned = new[] { new Marker(), new Marker(), new Marker() };
            var value = new Node { First = interned[2], Second = new Marker(), Next = new Node { First = interned[0], Second = interned[2] } };
            var many = Enumerable.Range(0, 5000).Select(_ => new Marker()).ToArray();

            byte[] expected;
            using (var fresh = GraphSerializer())
            {
                foreach (var marker in interned)
                {
                    fresh.Intern(marker);
                }

                expected = Write(fresh, value);
            }

            using var binary = GraphSerializer();
            binary.Intern(interned[0]);
            Write(binary, many);
            binary.Intern(interned[1]);
            binary.Intern(interned[2]);

            for (var pass = 0; pass < 2; ++pass)
            {
                Write(binary, value).Should().Equal(expected);
                Write(binary, value).Should().Equal(expected);
                var read = binary.Read<Node>(new MemoryStream(expected));
                read.First.Should().BeSameAs(interned[2]);
                read.Second.Should().NotBeSameAs(value.Second);
                read.Next!.First.Should().BeSameAs(interned[0]);
                read.Next.Second.Should().BeSameAs(interned[2]);

                binary.Read<Marker[]>(new MemoryStream(Write(binary, many))).Should().HaveCount(many.Length);
            }
        }

        private static IBinary GraphSerializer()
        {
            return Binary.Create(new Settings { SerializationMode = Mode.Graph }.MarkSerializable(_ => true));
        }

        private static byte[] Write<T>(IBinary binary, T value)
        {
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }
    }
}
