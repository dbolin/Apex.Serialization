using FluentAssertions;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// Before allocating an array of values that aren't copied as one block, the reader checks that the stream holds at
    /// least the fewest bytes its stored length of elements can take.  Every array here has each element at that fewest,
    /// so its own length is the longest the check accepts.
    /// </summary>
    public class ArrayLengthBoundTests
    {
        public sealed class Node
        {
            public int Value;
        }

        /// <summary>
        /// At least 13 bytes: an empty string's 4-byte length, a null Nullable's 1-byte flag and the long's 8.
        /// </summary>
        public struct Mixed
        {
            public string S;
            public int? N;
            public long L;
        }

        public struct Silent
        {
            public int Value;
        }

        private const int Count = 200_000;

        [Fact]
        public void AnArrayOfStructsWhoseCustomWriteWritesNothingReadsBackWhateverItsLength()
        {
            var silent = new Silent[Count];
            var settings = new Settings { UseSerializedVersionId = false, SupportSerializationHooks = true }
                .MarkSerializable(_ => true)
                .RegisterCustomSerializer<Silent>((_, _) => { }, (_, _) => { });
            using var binary = Binary.Create(settings);
            using var written = new MemoryStream();
            binary.Write(silent, written);
            written.Length.Should().BeLessThan(Count, "the elements take no bytes");

            binary.Read<Silent[]>(new MemoryStream(written.ToArray())).Should().Equal(silent);
        }

        [Fact]
        public void AnArrayOfNullNullablesRoundTripsAndOneElementMoreIsRejectedBeforeAllocating()
        {
            ShouldBeTheLongestLengthAccepted(new int?[Count]);
        }

        [Fact]
        public void AnArrayOfEmptyStringsRoundTripsAndOneElementMoreIsRejectedBeforeAllocating()
        {
            ShouldBeTheLongestLengthAccepted(Enumerable.Repeat("", Count).ToArray());
        }

        [Fact]
        public void AnArrayOfNullReferencesRoundTripsAndOneElementMoreIsRejectedBeforeAllocating()
        {
            ShouldBeTheLongestLengthAccepted(new Node?[Count]);
        }

        [Fact]
        public void AnArrayOfStructsAtTheSumOfTheirFieldsMinimumsRoundTripsAndOneElementMoreIsRejectedBeforeAllocating()
        {
            ShouldBeTheLongestLengthAccepted(Enumerable.Repeat(new Mixed { S = "" }, Count).ToArray());
        }

        private static IBinary Serializer() => Binary.Create(new Settings { UseSerializedVersionId = false }.MarkSerializable(_ => true));

        private static void ShouldBeTheLongestLengthAccepted<T>(T[] minimal)
        {
            using var binary = Serializer();
            using var written = new MemoryStream();
            binary.Write(minimal, written);
            var payload = written.ToArray();

            binary.Read<T[]>(new MemoryStream(payload)).Should().Equal(minimal);

            // A top-level array in Tree mode without version ids is its null flag, then its length.
            var longer = payload.ToArray();
            BinaryPrimitives.ReadInt32LittleEndian(longer.AsSpan(1)).Should().Be(minimal.Length);
            BinaryPrimitives.WriteInt32LittleEndian(longer.AsSpan(1), minimal.Length + 1);
            var stream = new MemoryStream(longer);

            var before = GC.GetAllocatedBytesForCurrentThread();
            Exception? exception = null;
            try
            {
                binary.Read<T[]>(stream);
            }
            catch (Exception e)
            {
                exception = e;
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            exception.Should().BeOfType<EndOfStreamException>();
            var arrayBytes = (minimal.Length + 1L) * Unsafe.SizeOf<T>();
            allocated.Should().BeLessThan(arrayBytes / 10, "the {0}-byte array wasn't allocated", arrayBytes);
            binary.Read<T[]>(new MemoryStream(payload)).Should().Equal(minimal);
        }
    }
}
