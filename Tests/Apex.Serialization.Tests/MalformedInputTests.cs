using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Xunit;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// Reads corrupted payloads of one graph (short strings, type ids, 1-, 2- and 3-dimensional arrays of values,
    /// references and structs, nested collections, a delegate with a target and a multicast one) and of single arrays and
    /// strings, over a seekable stream and over one that can't seek and supplies a few bytes per read.  Every read must
    /// end, with a value or with an exception that isn't an out-of-range index, without allocating anywhere near what a
    /// corrupt length claims, and leave the serializer reading intact data as before.
    /// </summary>
    public class MalformedInputTests
    {
        public sealed class Node
        {
            public int Id;
            public double Weight;
            public string? Name;
            public Node? Next;
        }

        public struct Tagged
        {
            public string Tag;
            public int Value;
        }

        public sealed class Counter
        {
            public int Count;

            public int Add(int x) => Count += x;

            public void Bump() => Count++;

            public void BumpTwice() => Count += 2;
        }

        public sealed class Graph
        {
            public Node? Root;
            public int[]? Numbers;
            public long[,]? Grid;
            public int[,,]? Cube;
            public string?[]? Words;
            public object?[]? Mixed;
            public Tagged[]? TaggedValues;
            public List<Dictionary<string, List<int>>>? Nested;
            public Func<int, int>? Adder;
            public Action? Bumps;
        }

        public static IEnumerable<object[]> Variants()
        {
            yield return new object[] { Mode.Tree, false, 3 };
            yield return new object[] { Mode.Graph, false, 3 };
            yield return new object[] { Mode.Graph, true, 3 };
            yield return new object[] { Mode.Graph, false, 0 };
        }

        // Far below any allocation a corrupt length could ask for, far above what generating code for a type costs.
        private const long AllocationLimit = 64L * 1024 * 1024;

        private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

        [Theory]
        [MemberData(nameof(Variants))]
        public void EveryTruncationThrows(Mode mode, bool versionIds, int inliningDepth)
        {
            var payload = Write(mode, versionIds, inliningDepth, BuildGraph());

            var cases = Enumerable.Range(0, payload.Length).Select(length => new Mutation($"truncated to {length} bytes", payload[..length]));

            var results = ReadAll<Graph>(mode, versionIds, inliningDepth, payload, cases);

            results.Where(x => x.Failure != null).Select(x => x.Failure).Should().BeEmpty();
            results.Where(x => x.Exception == null).Select(x => x.Mutation.Description).Should().BeEmpty("a truncated payload can't be read");
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void EveryTruncationOfAPayloadEndingInAFixedSizeValueThrows(Mode mode, bool versionIds, int inliningDepth)
        {
            // The stream ends inside the value's reservation, so its missing bytes are only caught after the read.
            var payload = Write(mode, versionIds, inliningDepth, (Id: 7, Weight: 0.25, Count: 0x0102030405060708L));
            using var binary = Serializer(mode, versionIds, inliningDepth);

            foreach (var length in Enumerable.Range(0, payload.Length))
            {
                var read = () => binary.Read<(int, double, long)>(new MemoryStream(payload[..length]));

                read.Should().Throw<Exception>("{0} of the {1} bytes are there", length, payload.Length);
            }
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void FlippedBytesReadOrThrow(Mode mode, bool versionIds, int inliningDepth)
        {
            var payload = Write(mode, versionIds, inliningDepth, BuildGraph());
            var random = new Random(1234);

            var cases = Enumerable.Range(0, 1500).Select(_ =>
            {
                var offset = random.Next(payload.Length);
                var flip = (byte)random.Next(1, 256);
                var mutated = payload.ToArray();
                mutated[offset] ^= flip;
                return new Mutation($"byte {offset} xor 0x{flip:X2}", mutated);
            });

            ReadAll<Graph>(mode, versionIds, inliningDepth, payload, cases).Where(x => x.Failure != null).Select(x => x.Failure).Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void OversizedLengthsAnywhereReadOrThrow(Mode mode, bool versionIds, int inliningDepth)
        {
            var payload = Write(mode, versionIds, inliningDepth, BuildGraph());
            var lengths = new[] { int.MaxValue, int.MinValue, -1, 0x40000000, Array.MaxLength };

            var cases = Enumerable.Range(0, payload.Length - 3).SelectMany(offset => lengths.Select(length =>
            {
                var mutated = payload.ToArray();
                BinaryPrimitives.WriteInt32LittleEndian(mutated.AsSpan(offset), length);
                return new Mutation($"0x{length:X8} at byte {offset}", mutated);
            }));

            ReadAll<Graph>(mode, versionIds, inliningDepth, payload, cases).Where(x => x.Failure != null).Select(x => x.Failure).Should().BeEmpty();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void AThreeDimensionalArrayWithOneEmptyDimensionAndTwoHugeOnesReadsBeforeTheDeadline(int emptyDimension)
        {
            var payload = Write(Mode.Tree, false, 3, new int[2, 3, 4]);
            var mutated = WithDimensions(payload, new[] { 2, 3, 4 }, Enumerable.Range(0, 3).Select(i => i == emptyDimension ? 0 : 0x40000000).ToArray());

            var results = ReadAll<int[,,]>(Mode.Tree, false, 3, payload, new[] { new Mutation($"dimension {emptyDimension} empty, the others 0x40000000", mutated) });

            results.Where(x => x.Failure != null).Select(x => x.Failure).Should().BeEmpty();
        }

        [Fact]
        public void A3DArrayWhoseElementCountOverflowsALongThrowsBeforeAllocatingIt()
        {
            // 2^90 elements, which a multiply that wraps counts as none.
            var payload = Write(Mode.Tree, false, 3, new int[2, 3, 4]);
            var mutated = WithDimensions(payload, new[] { 2, 3, 4 }, new[] { 0x40000000, 0x40000000, 0x40000000 });

            ShouldThrowWithoutAllocating<int[,,]>(payload, mutated, typeof(InvalidDataException));
        }

        [Fact]
        public void A2DBlittableArrayWhoseByteCountOverflowsALongThrowsBeforeAllocatingIt()
        {
            // 2^64 - 2^39 bytes of longs, which a multiply that wraps makes negative.
            var payload = Write(Mode.Tree, false, 3, new long[6, 7]);
            var mutated = WithDimensions(payload, new[] { 6, 7 }, new[] { 0x40000000, 0x7FFFFFC0 });

            ShouldThrowWithoutAllocating<long[,]>(payload, mutated, typeof(InvalidDataException));
        }

        [Fact]
        public void AnEmpty3DArrayReadsExactlyWhenTheRuntimeCanCreateIt()
        {
            int[] largest = { 65536, 65535, 0 };
            int[] pastIt = { 65536, 65536, 0 };
            var create = (int[] dimensions) => () => Array.CreateInstance(typeof(int), dimensions);
            create(largest).Should().NotThrow();
            create(pastIt).Should().Throw<OutOfMemoryException>();

            var payload = Write(Mode.Tree, false, 3, new int[2, 3, 4]);
            foreach (var open in StreamKinds)
            {
                using var binary = Serializer(Mode.Tree, false, 3);
                var read = binary.Read<int[,,]>(open(WithDimensions(payload, new[] { 2, 3, 4 }, largest)));
                var readPastIt = () => binary.Read<int[,,]>(open(WithDimensions(payload, new[] { 2, 3, 4 }, pastIt)));

                Enumerable.Range(0, 3).Select(read.GetLength).Should().Equal(largest);
                readPastIt.Should().Throw<InvalidDataException>();
            }
        }

        private const int StoredLength = 42;

        [Theory]
        [InlineData(0x3FFFFFDF)]
        [InlineData(0x40000000)]
        [InlineData(-2)]
        public void AStringLongerThanTheStreamThrowsBeforeAllocatingIt(int length)
        {
            ShouldThrowWithoutAllocating(new string('s', StoredLength), length, typeof(EndOfStreamException), typeof(InvalidDataException));
        }

        [Theory]
        [InlineData(int.MaxValue)]
        [InlineData(-5)]
        public void ABlittableArrayLongerThanTheStreamThrowsBeforeAllocatingIt(int length)
        {
            ShouldThrowWithoutAllocating(Enumerable.Range(0, StoredLength).ToArray(), length, typeof(EndOfStreamException), typeof(InvalidDataException));
        }

        [Theory]
        [InlineData(int.MaxValue)]
        [InlineData(-5)]
        public void AReferenceArrayLongerThanTheStreamThrowsBeforeAllocatingIt(int length)
        {
            ShouldThrowWithoutAllocating(Enumerable.Range(0, StoredLength).Select(x => x.ToString()).ToArray(), length, typeof(EndOfStreamException), typeof(InvalidDataException));
        }

        [Theory]
        [InlineData(int.MaxValue)]
        [InlineData(-5)]
        public void AStructArrayLongerThanTheStreamThrowsBeforeAllocatingIt(int length)
        {
            ShouldThrowWithoutAllocating(Enumerable.Range(0, StoredLength).Select(x => new Tagged { Tag = "t", Value = x }).ToArray(), length, typeof(EndOfStreamException), typeof(InvalidDataException));
        }

        [Fact]
        public void A2DArrayLongerThanTheStreamThrowsBeforeAllocatingIt()
        {
            var payload = Write(Mode.Tree, false, 3, new long[6, 7]);

            ShouldThrowWithoutAllocating<long[,]>(payload, WithDimensions(payload, new[] { 6, 7 }, new[] { 300_000_000, 7 }), typeof(EndOfStreamException));
        }

        [Fact]
        public void ATypeIdLongerThanTheBufferThrows()
        {
            // The type-id lengths of a polymorphic value follow its null flag and the -1 that marks a new type.
            var payload = Write(Mode.Tree, false, 3, (object)new Node());
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(1)).Should().Be(-1);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5), int.MaxValue);

            var read = () => Serializer(Mode.Tree, false, 3).Read<object>(new MemoryStream(payload));

            read.Should().Throw<InvalidDataException>();
        }

        private const int NullFlag = sizeof(byte);

        [Fact]
        public void APayloadCutInAPolymorphicValuesTypeReferenceThrowsEndOfStream()
        {
            var payload = Write(Mode.Tree, false, 3, (object)new Node());
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(NullFlag)).Should().Be(-1, "the precondition: the -1 that marks a new type follows the null flag");

            ShouldThrowEndOfStreamCutAnywhereInTheIdAt<object>(Mode.Tree, false, payload, NullFlag);
        }

        [Fact]
        public void APayloadCutInTheVersionIdAfterATypeIdThrowsEndOfStream()
        {
            var payload = Write(Mode.Tree, true, 3, (object)new Node());
            var versionId = TypeIdEnd(payload, NullFlag + sizeof(int));
            ShouldDifferOnlyByAVersionIdAt(payload, Write(Mode.Tree, false, 3, (object)new Node()), versionId);

            ShouldThrowEndOfStreamCutAnywhereInTheIdAt<object>(Mode.Tree, true, payload, versionId);
        }

        [Fact]
        public void APayloadCutInASealedReferencesVersionIdThrowsEndOfStream()
        {
            var payload = Write(Mode.Graph, true, 3, new Node());
            ShouldDifferOnlyByAVersionIdAt(payload, Write(Mode.Graph, false, 3, new Node()), NullFlag);

            ShouldThrowEndOfStreamCutAnywhereInTheIdAt<Node>(Mode.Graph, true, payload, NullFlag);
        }

        [Fact]
        public void APayloadCutInAValuesVersionIdThrowsEndOfStream()
        {
            var value = (Id: 7, Count: 8L);
            var payload = Write(Mode.Tree, true, 3, value);
            ShouldDifferOnlyByAVersionIdAt(payload, Write(Mode.Tree, false, 3, value), 0);

            ShouldThrowEndOfStreamCutAnywhereInTheIdAt<(int, long)>(Mode.Tree, true, payload, 0);
        }

        private static int TypeIdEnd(byte[] payload, int start)
        {
            var nameLength = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(start));
            var assemblyLength = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(start + sizeof(int)));
            return start + 2 * sizeof(int) + nameLength + ", ".Length + assemblyLength;
        }

        private static void ShouldDifferOnlyByAVersionIdAt(byte[] withVersionIds, byte[] withoutVersionIds, int offset)
        {
            withVersionIds[..offset].Should().Equal(withoutVersionIds[..offset]);
            withVersionIds[(offset + sizeof(int))..].Should().Equal(withoutVersionIds[offset..]);
        }

        private static void ShouldThrowEndOfStreamCutAnywhereInTheIdAt<T>(Mode mode, bool versionIds, byte[] payload, int offset)
        {
            using var binary = Serializer(mode, versionIds, 3);
            foreach (var open in StreamKinds)
            {
                foreach (var length in Enumerable.Range(offset, sizeof(int)))
                {
                    var stream = open(payload[..length]);

                    var exception = ReadCatching<T>(binary, stream);

                    exception.Should().BeOfType<EndOfStreamException>("the {0} ends {1} bytes into the four-byte id", stream.GetType().Name, length - offset);
                }
            }
        }

        [Fact]
        public void AFailedReadLeavesTheSerializerReadingTheNextPayloadCorrectly()
        {
            using var binary = Serializer(Mode.Graph, false, 3);
            var payload = Write(Mode.Graph, false, 3, BuildGraph());

            for (var length = 1; length < payload.Length; length += 37)
            {
                var truncated = () => binary.Read<Graph>(new MemoryStream(payload[..length]));
                truncated.Should().Throw<Exception>();

                var read = binary.Read<Graph>(new MemoryStream(payload));
                read.Mixed![0].Should().BeSameAs(read.Root, "a back-reference resolves within its own payload after {0} bytes of another failed", length);
                Write(Mode.Graph, false, 3, read).Should().Equal(payload);
            }
        }

        private static void ShouldThrowWithoutAllocating<T>(T value, int length, params Type[] expected)
        {
            var payload = Write(Mode.Tree, false, 3, value);

            ShouldThrowWithoutAllocating<T>(payload, WithDimensions(payload, new[] { StoredLength }, new[] { length }), expected);
        }

        private static void ShouldThrowWithoutAllocating<T>(byte[] payload, byte[] mutated, params Type[] expected)
        {
            foreach (var open in StreamKinds)
            {
                using var binary = Serializer(Mode.Tree, false, 3);
                RereadIntactFailure<T>(binary, payload, open).Should().BeNull("the first read generates the code, which isn't counted");

                var before = GC.GetAllocatedBytesForCurrentThread();
                var exception = ReadCatching<T>(binary, open(mutated));
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                exception.Should().NotBeNull();
                expected.Should().Contain(exception!.GetType(), exception.ToString());
                allocated.Should().BeLessThan(4 * 1024 * 1024);
                RereadIntactFailure<T>(binary, payload, open).Should().BeNull();
            }
        }

        /// <summary>
        /// Replaces a top-level array's or string's stored lengths, which in Tree mode without version ids follow its null
        /// flag, after checking they are the ones the value was written with.
        /// </summary>
        private static byte[] WithDimensions(byte[] payload, int[] written, int[] replacements)
        {
            var mutated = payload.ToArray();
            for (var i = 0; i < written.Length; ++i)
            {
                var at = mutated.AsSpan(1 + sizeof(int) * i);
                BinaryPrimitives.ReadInt32LittleEndian(at).Should().Be(written[i]);
                BinaryPrimitives.WriteInt32LittleEndian(at, replacements[i]);
            }

            return mutated;
        }

        private static readonly Func<byte[], Stream>[] StreamKinds = { x => new MemoryStream(x), x => new TrickleStream(x) };

        private static Exception? ReadCatching<T>(IBinary binary, Stream stream)
        {
            try
            {
                binary.Read<T>(stream);
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        private static string? RereadIntactFailure<T>(IBinary binary, byte[] payload, Func<byte[], Stream> open)
        {
            try
            {
                var value = binary.Read<T>(open(payload));
                using var rewritten = new MemoryStream();
                binary.Write(value, rewritten);
                return rewritten.ToArray().AsSpan().SequenceEqual(payload) ? null : "the intact payload rewrote to different bytes";
            }
            catch (Exception e)
            {
                return $"the intact payload then failed: {e}";
            }
        }

        private static Graph BuildGraph()
        {
            var counter = new Counter();
            var leaf = new Node { Id = 3, Weight = 0.5, Name = "leaf" };
            var root = new Node { Id = 1, Weight = 2.5, Name = "root", Next = new Node { Id = 2, Name = null, Next = leaf } };
            return new Graph
            {
                Root = root,
                Numbers = Enumerable.Range(0, 20).ToArray(),
                Grid = new long[,] { { 1, 2, 3 }, { 4, 5, 6 } },
                Cube = new int[2, 2, 2] { { { 1, 2 }, { 3, 4 } }, { { 5, 6 }, { 7, 8 } } },
                Words = new[] { "alpha", null, "", "gamma" },
                Mixed = new object?[] { root, 7, "eight", null, new[] { 9, 10 }, leaf },
                TaggedValues = new[] { new Tagged { Tag = "x", Value = 1 }, new Tagged { Tag = "y", Value = 2 } },
                Nested = new List<Dictionary<string, List<int>>>
                {
                    new Dictionary<string, List<int>> { ["a"] = new List<int> { 1, 2 }, ["b"] = new List<int>() },
                    new Dictionary<string, List<int>>(),
                },
                Adder = counter.Add,
                Bumps = (Action)counter.Bump + counter.BumpTwice,
            };
        }

        private static IBinary Serializer(Mode mode, bool versionIds, int inliningDepth)
        {
            return Binary.Create(new Settings
            {
                SerializationMode = mode,
                UseSerializedVersionId = versionIds,
                InliningMaxDepth = inliningDepth,
                AllowFunctionSerialization = true,
            }.MarkSerializable(_ => true));
        }

        private static byte[] Write<T>(Mode mode, bool versionIds, int inliningDepth, T value)
        {
            using var binary = Serializer(mode, versionIds, inliningDepth);
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }

        private sealed record Mutation(string Description, byte[] Payload);

        private sealed record Result(Mutation Mutation, Exception? Exception, string? Failure);

        /// <summary>
        /// Reads every mutation over both kinds of stream on one thread, so a read that never ends fails the test at
        /// <see cref="Deadline"/> instead of hanging it.
        /// </summary>
        private static List<Result> ReadAll<T>(Mode mode, bool versionIds, int inliningDepth, byte[] payload, IEnumerable<Mutation> mutations)
        {
            var results = new List<Result>();
            var current = "";
            var reader = new Thread(() =>
            {
                using var binary = Serializer(mode, versionIds, inliningDepth);
                foreach (var mutation in mutations)
                {
                    Volatile.Write(ref current, mutation.Description);
                    foreach (var open in StreamKinds)
                    {
                        results.Add(Read<T>(binary, mutation, payload, open));
                    }
                }
            }) { IsBackground = true };
            reader.Start();

            reader.Join(Deadline).Should().BeTrue($"every read ends, but the one {Volatile.Read(ref current)} didn't");
            return results;
        }

        private static Result Read<T>(IBinary binary, Mutation mutation, byte[] payload, Func<byte[], Stream> open)
        {
            var stream = open(mutation.Payload);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var exception = ReadCatching<T>(binary, stream);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // The reader raises InvalidDataException or EndOfStreamException for data it rejects, so an out-of-range index
            // is a length or reference it took without checking.  Debug builds also check that every value read lies inside
            // a reservation, which keeps it inside the buffer.
            var missedCheck = exception is OutOfMemoryException or IndexOutOfRangeException or ArgumentOutOfRangeException
                || exception?.Message == BufferedStream.ReservationOverrun
                || allocated > AllocationLimit;
            string? failure = null;
            if (missedCheck)
            {
                failure = $"{mutation.Description} over {stream.GetType().Name} allocated {allocated} bytes: {exception?.ToString() ?? "read"}";
            }
            else if (exception != null && RereadIntactFailure<T>(binary, payload, open) is { } reread)
            {
                failure = $"{mutation.Description} over {stream.GetType().Name}: {reread}";
            }

            return new Result(mutation, exception, failure);
        }

        /// <summary>
        /// Can't seek, and supplies at most a few bytes per read, so every refill, read-ahead and length check runs on the
        /// path a decompressing or network stream takes.
        /// </summary>
        private sealed class TrickleStream(byte[] data) : Stream
        {
            private int _position;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = Math.Min(Math.Min(count, 7), data.Length - _position);
                data.AsSpan(_position, read).CopyTo(buffer.AsSpan(offset));
                _position += read;
                return read;
            }

            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
