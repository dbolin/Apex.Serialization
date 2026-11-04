using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// A custom instantiator may read from the stream before the reader fills in the instance's fields, so the fields'
    /// reads can't rely on the bytes reserved before the instantiator ran.
    /// </summary>
    public class ReadingInstantiatorTests
    {
        public sealed class Pair
        {
            public long First;
            public long Second;

            [NonSerialized]
            public int Header;
        }

        public sealed class Batch
        {
            public byte[]? Padding;
            public Pair[]? Pairs;
        }

        // Its null flag, the header its instantiator reads, then its fields.
        private const int PairBytes = 1 + sizeof(int) + 2 * sizeof(long);

        // Writes the header an instantiator reads ahead of the fields the reader then reads itself.
        private static IBinary Writer() => Binary.Create(new Settings { UseSerializedVersionId = false, SupportSerializationHooks = true }
            .MarkSerializable(typeof(Batch))
            .MarkSerializable(typeof(Pair))
            .RegisterCustomSerializer<Pair>((pair, writer) =>
            {
                writer.Write(pair.Header);
                writer.Write(pair.First);
                writer.Write(pair.Second);
            }, (_, _) => { }));

        private static IBinary Reader() => Binary.Create(new Settings { UseSerializedVersionId = false, SupportSerializationHooks = true }
            .MarkSerializable(typeof(Batch))
            .MarkSerializable(typeof(Pair))
            .RegisterCustomInstantiator<Pair>(reader => new Pair { Header = reader.Read<int>() }));

        [Fact]
        public void FieldsReadAfterAReadingInstantiatorRoundTripAcrossTheBufferEnd()
        {
            var count = 2 * BufferedStream.MaxSize / PairBytes;
            var pairs = Enumerable.Range(0, count).Select(i => new Pair { Header = i, First = (long)i << 32 | 0x5A5A, Second = -i }).ToArray();
            using var writer = Writer();
            using var reader = Reader();

            // Each padding length lands a pair across the end of the buffer at a different offset.
            foreach (var padding in Enumerable.Range(0, PairBytes))
            {
                var batch = new Batch { Padding = new byte[padding], Pairs = pairs };
                using var stream = new MemoryStream();
                writer.Write(batch, stream);
                stream.Position = 0;

                var read = reader.Read<Batch>(stream);

                read.Pairs!.Select(Fields).Should().Equal(pairs.Select(Fields), "with {0} bytes of padding", padding);
            }
        }

        private static (int, long, long) Fields(Pair pair) => (pair.Header, pair.First, pair.Second);
    }
}
