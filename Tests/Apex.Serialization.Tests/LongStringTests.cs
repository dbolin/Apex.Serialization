using FluentAssertions;
using System;
using System.IO;
using Xunit;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// A string longer than the stream buffer is read from a seekable stream straight into the string, after checking the
    /// stream holds all of it, and from any other stream into characters grown as its bytes arrive.
    /// </summary>
    public class LongStringTests
    {
        private static string LongString(int seed)
        {
            return string.Create(BufferedStream.MaxSize, seed, (chars, s) =>
            {
                for (var i = 0; i < chars.Length; ++i)
                {
                    chars[i] = (char)(0x2000 + (i * 31 + s) % 0x1000);
                }
            });
        }

        private static IBinary Serializer() => Binary.Create(new Settings { UseSerializedVersionId = false });

        private static byte[] Write<T>(T value)
        {
            using var binary = Serializer();
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void StringsLongerThanTheBufferRoundTripAmongShortOnes(bool seekable)
        {
            var strings = new[] { LongString(1), "between", LongString(2), "after" };
            var payload = Write(strings);
            using var binary = Serializer();

            var read = binary.Read<string[]>(seekable ? new MemoryStream(payload) : new NonSeekableStream(payload));

            read.Should().Equal(strings);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(BufferedStream.MaxSize)]
        public void ATruncatedLongStringOnASeekableStreamThrowsBeforeAllocatingIt(int missingBytes)
        {
            var text = LongString(3);
            var payload = Write(text);
            using var binary = Serializer();
            binary.Read<string>(new MemoryStream(payload)).Should().Be(text, "the first read generates the code, which isn't counted");
            var truncated = new MemoryStream(payload, 0, payload.Length - missingBytes);

            var before = GC.GetAllocatedBytesForCurrentThread();
            Exception? exception = null;
            try
            {
                binary.Read<string>(truncated);
            }
            catch (Exception e)
            {
                exception = e;
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            exception.Should().BeOfType<EndOfStreamException>();
            allocated.Should().BeLessThan(text.Length, "the string's {0} bytes weren't allocated", 2 * text.Length);
            binary.Read<string>(new MemoryStream(payload)).Should().Be(text);
        }

        private sealed class NonSeekableStream(byte[] data) : Stream
        {
            private readonly MemoryStream _inner = new MemoryStream(data);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
