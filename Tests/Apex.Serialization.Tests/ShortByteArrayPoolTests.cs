using FluentAssertions;
using System;
using System.Buffers;
using System.IO;
using Xunit;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization.Tests
{
    [CollectionDefinition(nameof(ByteArrayPoolCollection), DisableParallelization = true)]
    public class ByteArrayPoolCollection
    {
    }

    /// <summary>
    /// A byte pool that hands back less than it was asked for leaves the stream sized for a buffer it doesn't have, so
    /// only the bounds check on each write stops the first value that would end past the buffer.  The pool is global, so
    /// these tests run apart from the others.
    /// </summary>
    [Collection(nameof(ByteArrayPoolCollection))]
    public class ShortByteArrayPoolTests
    {
        // Not a multiple of 8, so a write that slipped past the check would land in the array's alignment padding.
        private const int ShortLength = 61;

        [Fact]
        public void AWriteThatWouldEndPastAShortPooledBufferThrows()
        {
            // Not disposed: Dispose would return the buffer to the pool current by then, the shared one.
            var stream = CreateWithPool(new ShortPool());
            stream.WriteTo(new MemoryStream());
            var valuesWritten = 0;

            var writeOn = () =>
            {
                while (true)
                {
                    stream.ReserveSize(sizeof(int));
                    stream.Write(0);
                    ++valuesWritten;
                }
            };

            writeOn.Should().Throw<ArgumentOutOfRangeException>();
            valuesWritten.Should().Be(ShortLength / sizeof(int), "the next int would end 3 bytes past the buffer");
            var writeTheLastByte = () =>
            {
                stream.ReserveSize(sizeof(byte));
                stream.Write((byte)0);
            };
            writeTheLastByte.Should().NotThrow("a value that ends on the buffer's last byte is inside it");
        }

        private static BufferedStream CreateWithPool(ArrayPool<byte> pool)
        {
            var previous = Binary.ByteArrayPool;
            Binary.SetByteArrayPool(pool);
            try
            {
                return BufferedStream.Create();
            }
            finally
            {
                Binary.SetByteArrayPool(previous);
            }
        }

        private sealed class ShortPool : ArrayPool<byte>
        {
            public override byte[] Rent(int minimumLength) => new byte[ShortLength];

            public override void Return(byte[] array, bool clearArray = false)
            {
            }
        }
    }
}
