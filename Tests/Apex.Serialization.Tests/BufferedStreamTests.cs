using System;
using System.IO;
using System.Runtime.CompilerServices;
using Apex.Serialization.Internal;
using FluentAssertions;
using Xunit;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization.Tests
{
    public class BufferedStreamTests : IDisposable
    {
        private MemoryStream memoryStream = new MemoryStream();
        internal IBufferedStream Sut;

        public BufferedStreamTests()
        {
            Sut = new BufferedStream();
            Sut.WriteTo(memoryStream);
        }

        public void Dispose()
        {
            Sut.Dispose();
        }

        [Fact]
        public void FillWith1And0()
        {
            for (int j = 0; j < 3; ++j)
            {
                Sut.WriteTo(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);
                for (int i = 0; i < 100000; ++i)
                {
                    Sut.ReserveSize(4);
                    Sut.Write(1);
                }

                Sut.Flush();

                var bytes = memoryStream.ToArray();
                for (int i = 0; i < bytes.Length; ++i)
                {
                    bytes[i].Should().Be((i % 4) == 0 ? (byte) 1 : (byte) 0);
                }

                memoryStream.Seek(0, SeekOrigin.Begin);
                for (int i = 0; i < 100000; ++i)
                {
                    Sut.ReserveSize(4);
                    Sut.Write(0);
                }

                Sut.Flush();

                bytes = memoryStream.ToArray();
                for (int i = 0; i < bytes.Length; ++i)
                {
                    bytes[i].Should().Be(0);
                }
            }
        }

        [Fact]
        public void WriteStrings()
        {
            for (int i = 0; i < 100000; ++i)
            {
                Sut.WriteTo(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);
                Sut.Write("asd");
                Sut.Write("zzzz");

                Sut.Flush();

                var bytes = memoryStream.ToArray();
                bytes[0].Should().Be(3);
                bytes[1].Should().Be(0);
                bytes[2].Should().Be(0);
                bytes[3].Should().Be(0);
                bytes[4].Should().Be((byte)'a');
                bytes[5].Should().Be(0);
                bytes[6].Should().Be((byte)'s');
                bytes[7].Should().Be(0);
                bytes[8].Should().Be((byte)'d');
                bytes[9].Should().Be(0);
                bytes[10].Should().Be(4);
                bytes[11].Should().Be(0);
                bytes[12].Should().Be(0);
                bytes[13].Should().Be(0);
                bytes[14].Should().Be((byte)'z');
                bytes[15].Should().Be(0);
                bytes[16].Should().Be((byte)'z');
                bytes[17].Should().Be(0);
                bytes[18].Should().Be((byte)'z');
                bytes[19].Should().Be(0);
                bytes[20].Should().Be((byte)'z');
                bytes[21].Should().Be(0);
            }
        }

        [Fact]
        public unsafe void WriteType()
        {
            Sut.WriteTo(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);

            Sut.WriteTypeId(typeof(BufferedStreamTests));
            Sut.Flush();

            memoryStream.Seek(0, SeekOrigin.Begin);
            Sut.ReadFrom(memoryStream);

            var ptr = Sut.ReadTypeId(out var len1, out var len2);

            len1.Should().Be(44);
            len2.Should().Be(79);

            var type = Sut.RestoreTypeFromId(ref ptr, len1, len2);
            type.AssemblyQualifiedName.Should().Be(typeof(BufferedStreamTests).AssemblyQualifiedName);

            Sut.Flush();

            Sut.WriteTo(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);

            Sut.WriteTypeId(typeof(AbstractSerializerTestBase));
            Sut.Flush();

            memoryStream.Seek(0, SeekOrigin.Begin);
            Sut.ReadFrom(memoryStream);

            ptr = Sut.ReadTypeId(out var len3, out var len4);

            len3.Should().Be(51);
            len4.Should().Be(79);

            type = Sut.RestoreTypeFromId(ref ptr, len3, len4);
            type.AssemblyQualifiedName.Should().Be(typeof(AbstractSerializerTestBase).AssemblyQualifiedName);

            Sut.Flush();
        }

        [Fact]
        public void Wrapping()
        {
            memoryStream.Seek(0, SeekOrigin.Begin);
            Sut.WriteTo(memoryStream);

            var n = 200000;

            for (int i = 0; i < n; ++i)
            {
                Sut.ReserveSize(4);
                Sut.Write(i);
            }
            Sut.Flush();

            memoryStream.Seek(0, SeekOrigin.Begin);
            Sut.ReadFrom(memoryStream);

            for (int i = 0; i < n; ++i)
            {
                Sut.ReserveSize(4);
                Sut.Read<int>().Should().Be(i);
            }

            memoryStream.Seek(0, SeekOrigin.Begin);
            Sut.WriteTo(memoryStream);

            for (int i = 0; i < n; ++i)
            {
                Sut.Write($"00000000000000000000{i}");
            }
            Sut.Flush();

            memoryStream.Seek(0, SeekOrigin.Begin);

            Sut.ReadFrom(memoryStream);

            for (int i = 0; i < n; ++i)
            {
                var x = Sut.Read();
                x.Should().Be($"00000000000000000000{i}");
            }
        }
    }
}
