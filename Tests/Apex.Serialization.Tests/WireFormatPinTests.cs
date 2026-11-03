using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// Pins the exact bytes of an object written through a polymorphic reference: its type ids, serialized version ids and
    /// fields, including a nested polymorphic field and a blittable array, so a diff here means the wire format changed.
    /// </summary>
    public class WireFormatPinTests
    {
        public class PinShape
        {
            public int Id;
            public string? Label;
            public PinShape? Next;
        }

        public sealed class PinCircle : PinShape
        {
            public double Radius;
            public long[]? Samples;
        }

        private static object BuildPinnedShape()
        {
            return new PinCircle
            {
                Id = 1,
                Label = "circle",
                Radius = 2.5,
                Samples = new long[] { 3, -4 },
                Next = new PinShape { Id = 2, Label = null, Next = null },
            };
        }

        private static string WriteHex(Mode mode)
        {
            var settings = new Settings { SerializationMode = mode }
                .MarkSerializable(typeof(PinShape))
                .MarkSerializable(typeof(PinCircle));
            using var sut = Binary.Create(settings);
            using var stream = new MemoryStream();
            sut.Write(BuildPinnedShape(), stream);
            return Convert.ToHexString(stream.ToArray());
        }

        private const string ExpectedTreeBytes =
            "01FFFFFFFF350000005B0000006570637869536E725061736973615469696E74546D736F73655769722E46747265612E"
            + "506F6E74657A746C2B696965432E72656C412C20347063783153367232616569346133693D6E65546F7379734B206965"
            + "6273506F2C3D612E742E652E3D2C7243746C75752065306E30753072316C6E206975726C56632C657454656B2E6E6F62"
            + "74307A376C34693465322E39653241AEC997910100000000000000000004400600000063006900720063006C00650001"
            + "FFFFFFFF340000005B00000065706178535369722B61746965616E69506E6154727346737257692E65746F656D2E746F"
            + "6974547A736C736950656E2E686570412C20347063783153367232616569346133693D6E65546F7379734B2069656273"
            + "506F2C3D612E742E652E3D2C7243746C75752065306E30753072316C6E206975726C56632C657454656B2E6E6F627430"
            + "7A376C34693465322E396532419F05BEFC02000000FFFFFFFF0001020000000300000000000000FCFFFFFFFFFFFFFF";

        private const string ExpectedGraphBytes =
            "01FFFFFFFFFFFFFFFF350000005B0000006570637869536E725061736973615469696E74546D736F73655769722E4674"
            + "7265612E506F6E74657A746C2B696965432E72656C412C20347063783153367232616569346133693D6E65546F737973"
            + "4B2069656273506F2C3D612E742E652E3D2C7243746C75752065306E30753072316C6E206975726C56632C657454656B"
            + "2E6E6F6274307A376C34693465322E39653241EBA79EE00100000000000000000004400600000063006900720063006C"
            + "00650001FFFFFFFFFFFFFFFF340000005B00000065706178535369722B61746965616E69506E6154727346737257692E"
            + "65746F656D2E746F6974547A736C736950656E2E686570412C20347063783153367232616569346133693D6E65546F73"
            + "79734B2069656273506F2C3D612E742E652E3D2C7243746C75752065306E30753072316C6E206975726C56632C657454"
            + "656B2E6E6F6274307A376C34693465322E396532412DB9DE1902000000FFFFFFFF0001FFFFFFFF020000000300000000"
            + "000000FCFFFFFFFFFFFFFF";

        [Fact]
        public void TreeModeBytesUnchanged()
        {
            WriteHex(Mode.Tree).Should().Be(ExpectedTreeBytes);
        }

        [Fact]
        public void GraphModeBytesUnchanged()
        {
            WriteHex(Mode.Graph).Should().Be(ExpectedGraphBytes);
        }

        [Theory]
        [InlineData(Mode.Tree, ExpectedTreeBytes)]
        [InlineData(Mode.Graph, ExpectedGraphBytes)]
        public void PinnedBytesReadBackToTheObject(Mode mode, string hex)
        {
            var settings = new Settings { SerializationMode = mode }
                .MarkSerializable(typeof(PinShape))
                .MarkSerializable(typeof(PinCircle));
            using var sut = Binary.Create(settings);

            var read = sut.Read<object>(new MemoryStream(Convert.FromHexString(hex)));

            read.Should().BeOfType<PinCircle>().Which.Should().BeEquivalentTo((PinCircle)BuildPinnedShape(), options => options.PreferringRuntimeMemberTypes());
        }
    }
}
