using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace Apex.Serialization.Tests
{
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

        public sealed class PinListener
        {
            public int Id;

            public void OnFirst()
            {
            }

            public void OnSecond()
            {
            }

            public static void OnAny()
            {
            }
        }

        public delegate void PinHandler();

        public sealed class PinHandlers
        {
            public PinHandler? WithTarget;
            public PinHandler? WithoutTarget;
            public PinHandler? Multicast;
        }

        private static string WriteDelegatesHex(Mode mode)
        {
            var listener = new PinListener { Id = 3 };
            var handlers = new PinHandlers
            {
                WithTarget = listener.OnFirst,
                WithoutTarget = PinListener.OnAny,
                Multicast = (PinHandler)listener.OnFirst + listener.OnSecond + PinListener.OnAny,
            };
            var settings = new Settings { SerializationMode = mode, AllowFunctionSerialization = true }
                .MarkSerializable(typeof(PinListener))
                .MarkSerializable(typeof(PinHandlers));
            using var sut = Binary.Create(settings);
            using var stream = new MemoryStream();
            sut.Write(handlers, stream);
            return Convert.ToHexString(stream.ToArray());
        }

        private const string ExpectedTreeDelegateBytes =
            "0100D3CA8101FFFFFFFF360000005B00000072706C786E53487269612B69746165696E6E50546173727346577269652E"
            + "6F746D65742E696F5474737A736C50696E65612E646565412C20347063783153367232616569346133693D6E65546F73"
            + "79734B2069656273506F2C3D612E742E652E3D2C7243746C75752065306E30753072316C6E206975726C56632C657454"
            + "656B2E6E6F6274307A376C34693465322E396532415533B4E401000000FFFFFFFF370000005B00000072706E78745369"
            + "726E61506973617369546E695474736D736F57657269462E72746165502E6E6F6574747A2B6C69694C65732E65656541"
            + "2C20347063783153367232616569346133693D6E65546F7379734B2069656273506F2C3D612E742E652E3D2C7243746C"
            + "75752065306E30753072316C6E206975726C56632C657454656B2E6E6F6274307A376C34693465322E39653241050000"
            + "004F006E0041006E00790000000001030000000100000002000000070000004F006E0046006900720073007400000001"
            + "01020000000300000001000000000100000002000000080000004F006E005300650063006F006E006400000001010200"
            + "00000300000001000000000100000002000000050000004F006E0041006E007900000000010000000001010000000100"
            + "000002000000050000004F006E0041006E007900000000010000000001010000000100000002000000070000004F006E"
            + "00460069007200730074000000010102000000030000000100000000";

        private const string ExpectedGraphDelegateBytes =
            "014E0547E2FFFFFFFF01FFFFFFFFFFFFFFFF360000005B00000072706C786E53487269612B69746165696E6E50546173"
            + "727346577269652E6F746D65742E696F5474737A736C50696E65612E646565412C203470637831533672326165693461"
            + "33693D6E65546F7379734B2069656273506F2C3D612E742E652E3D2C7243746C75752065306E30753072316C6E206975"
            + "726C56632C657454656B2E6E6F6274307A376C34693465322E39653241B56DE29B01000000FFFFFFFF370000005B0000"
            + "0072706E78745369726E61506973617369546E695474736D736F57657269462E72746165502E6E6F6574747A2B6C6969"
            + "4C65732E656565412C20347063783153367232616569346133693D6E65546F7379734B2069656273506F2C3D612E742E"
            + "652E3D2C7243746C75752065306E30753072316C6E206975726C56632C657454656B2E6E6F6274307A376C3469346532"
            + "2E39653241050000004F006E0041006E00790000000001030000000100000002000000070000004F006E004600690072"
            + "007300740000000101FFFFFFFF020000000300000001000000000100000002000000080000004F006E00530065006300"
            + "6F006E006400000001010200000001000000000100000002000000050000004F006E0041006E00790000000001000000"
            + "0001FFFFFFFF010000000100000002000000050000004F006E0041006E007900000000010000000001FFFFFFFF010000"
            + "000100000002000000070000004F006E004600690072007300740000000101020000000100000000";

        [Fact]
        public void TreeModeDelegateBytesUnchanged()
        {
            WriteDelegatesHex(Mode.Tree).Should().Be(ExpectedTreeDelegateBytes);
        }

        [Fact]
        public void GraphModeDelegateBytesUnchanged()
        {
            WriteDelegatesHex(Mode.Graph).Should().Be(ExpectedGraphDelegateBytes);
        }
    }
}
