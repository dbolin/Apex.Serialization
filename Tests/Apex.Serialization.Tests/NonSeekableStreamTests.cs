using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// A stream that can't seek can't vouch for a length without reading what it implies, so a blittable array or a string
    /// longer than the stream buffer is read into an allocation grown as its bytes arrive, and the buffer the stream is
    /// read through keeps its size.
    /// </summary>
    public class NonSeekableStreamTests
    {
        private const int Elements = 4 * BufferedStream.MaxSize;

        private const int Rows = 2048;

        [Fact]
        public void AnArrayOfValuesLongerThanTheBufferReadsWithoutGrowingIt()
        {
            var values = Enumerable.Range(0, Elements).Select(i => i * 31L).ToArray();

            var (read, bufferLengths) = ReadThroughNonSeekableStream(values);

            read.AsSpan().SequenceEqual(values).Should().BeTrue();
            bufferLengths.Should().Equal(BufferedStream.MaxSize);
        }

        [Fact]
        public void ATwoDimensionalArrayOfValuesLongerThanTheBufferReadsWithoutGrowingIt()
        {
            var values = new long[Rows, Elements / Rows];
            for (var i = 0; i < values.GetLength(0); ++i)
            {
                for (var j = 0; j < values.GetLength(1); ++j)
                {
                    values[i, j] = i * 31L + j;
                }
            }

            var (read, bufferLengths) = ReadThroughNonSeekableStream(values);

            read.Cast<long>().SequenceEqual(values.Cast<long>()).Should().BeTrue();
            bufferLengths.Should().Equal(BufferedStream.MaxSize);
        }

        [Fact]
        public void AStringLongerThanTheBufferReadsWithoutGrowingIt()
        {
            var text = string.Create(Elements, 0, (chars, _) =>
            {
                for (var i = 0; i < chars.Length; ++i)
                {
                    chars[i] = (char)(0x2000 + i % 0x1000);
                }
            });

            var (read, bufferLengths) = ReadThroughNonSeekableStream(text);

            read.Should().Be(text);
            bufferLengths.Should().Equal(BufferedStream.MaxSize);
        }

        [Fact]
        public void AReadThatGrowsTheBufferToCheckAnArraysLengthLeavesTheNextReadOnTheOriginalBuffer()
        {
            using var binary = Binary.Create(new Settings { UseSerializedVersionId = false });
            var growing = new BufferRecordingStream(Written(binary, new string?[2 * BufferedStream.MaxSize]));
            var next = new BufferRecordingStream(Written(binary, 42));

            binary.Read<string?[]>(growing).Should().HaveCount(2 * BufferedStream.MaxSize);
            binary.Read<int>(next).Should().Be(42);

            growing.BufferLengths.Max().Should().BeGreaterThan(BufferedStream.MaxSize);
            next.BufferLengths.Should().Equal(BufferedStream.MaxSize);
        }

        /// <summary>
        /// No read follows to release the buffer when it starts, so only the failed read's own cleanup can.
        /// </summary>
        [Fact]
        public void AReadThatGrowsTheBufferAndThenFailsLetsTheGrownBufferBeCollected()
        {
            using var binary = Binary.Create(new Settings { UseSerializedVersionId = false });
            var whole = Written(binary, new string?[2 * BufferedStream.MaxSize]);

            var grown = ReadFailingAfterGrowth(binary, whole.AsSpan(0, 3 * BufferedStream.MaxSize / 2).ToArray());
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            grown.TryGetTarget(out _).Should().BeFalse("the failed read released the buffer it grew");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference<byte[]> ReadFailingAfterGrowth(IBinary binary, byte[] truncated)
        {
            var stream = new BufferRecordingStream(truncated);

            var read = () => binary.Read<string?[]>(stream);

            read.Should().Throw<EndOfStreamException>();
            stream.LargestBuffer!.TryGetTarget(out var largest).Should().BeTrue();
            largest!.Length.Should().BeGreaterThan(BufferedStream.MaxSize);
            return stream.LargestBuffer;
        }

        private static byte[] Written<T>(IBinary binary, T value)
        {
            using var written = new MemoryStream();
            binary.Write(value, written);
            return written.ToArray();
        }

        private static (T Read, IEnumerable<int> BufferLengths) ReadThroughNonSeekableStream<T>(T value)
        {
            using var binary = Binary.Create(new Settings { UseSerializedVersionId = false });
            using var written = new MemoryStream();
            binary.Write(value, written);
            var stream = new BufferRecordingStream(written.ToArray());

            var read = binary.Read<T>(stream);

            return (read, stream.BufferLengths.Order());
        }

        /// <summary>
        /// Can't seek, and records the length of every buffer it's asked to read into and, weakly, the largest one.
        /// </summary>
        private sealed class BufferRecordingStream(byte[] data) : Stream
        {
            private readonly MemoryStream _inner = new MemoryStream(data);

            public HashSet<int> BufferLengths { get; } = new HashSet<int>();

            public WeakReference<byte[]>? LargestBuffer { get; private set; }

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
                BufferLengths.Add(buffer.Length);
                if (LargestBuffer == null || !LargestBuffer.TryGetTarget(out var largest) || buffer.Length > largest.Length)
                {
                    LargestBuffer = new WeakReference<byte[]>(buffer);
                }
                return _inner.Read(buffer, offset, count);
            }

            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
