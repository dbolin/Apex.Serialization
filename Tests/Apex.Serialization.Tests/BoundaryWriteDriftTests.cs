using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// Pins the exact bytes plain (non-boundary) settings produce for a representative graph.
    /// The expected values were captured before serialization-boundary support was added, so a
    /// diff here means the write tree drifted for consumers that never mark a boundary type.
    /// </summary>
    public class BoundaryWriteDriftTests
    {
        public sealed class PinLeaf
        {
            public int Number;
            public string? Text;
        }

        public sealed class PinNode
        {
            public double Value;
            public PinLeaf? Leaf;
            public PinNode? Next;
        }

        public class PinBase
        {
            public int BaseNumber;
            public string? BaseText;
        }

        public sealed class PinDerived : PinBase
        {
            public float DerivedValue;
        }

        public sealed class PinRoot
        {
            public int Count;
            public string? Name;
            public int[]? Numbers;
            public PinLeaf? Shared;
            public PinNode? Head;
            public PinDerived? Derived;
        }

        internal static PinRoot BuildPinnedGraph()
        {
            var shared = new PinLeaf { Number = 7, Text = "leaf" };
            var tail = new PinNode { Value = 2.5, Leaf = shared, Next = null };
            var head = new PinNode { Value = 1.25, Leaf = shared, Next = tail };

            return new PinRoot
            {
                Count = 3,
                Name = "root",
                Numbers = new[] { 1, 2, 3 },
                Shared = shared,
                Head = head,
                // A base/derived pair: the write-side boundary early-out wraps the base-chain walk, so
                // that is where drift would show up under FlattenClassHierarchy = false.
                Derived = new PinDerived { BaseNumber = 4, BaseText = "base", DerivedValue = 0.5f },
            };
        }

        internal static Settings PinnedSettings(Mode mode)
        {
            return new Settings { SerializationMode = mode }
                .MarkSerializable(typeof(PinLeaf))
                .MarkSerializable(typeof(PinNode))
                .MarkSerializable(typeof(PinBase))
                .MarkSerializable(typeof(PinDerived))
                .MarkSerializable(typeof(PinRoot));
        }

        internal static string WriteHex(Settings settings, PinRoot graph)
        {
            using var sut = Binary.Create(settings);
            using var stream = new MemoryStream();
            sut.Write(graph, stream);
            return Convert.ToHexString(stream.ToArray());
        }

        private const string ExpectedTreeBytes =
            "01661B3AA2030000000400000072006F006F00740001040000000000003F04000000620061007300650001000000"
            + "000000F43F0107000000040000006C006500610066000100000000000004400107000000040000006C0065006100"
            + "66000001030000000100000002000000030000000107000000040000006C00650061006600";

        private const string ExpectedGraphBytes =
            "01067E29FAFFFFFFFF030000000400000072006F006F00740001FFFFFFFF040000000000003F040000006200610073"
            + "00650001FFFFFFFF000000000000F43F01FFFFFFFF07000000040000006C0065006100660001FFFFFFFF00000000"
            + "0000044001040000000001FFFFFFFF030000000100000002000000030000000104000000";

        [Fact]
        public void TreeModeWriteBytesUnchanged()
        {
            WriteHex(PinnedSettings(Mode.Tree), BuildPinnedGraph()).Should().Be(ExpectedTreeBytes);
        }

        [Fact]
        public void GraphModeWriteBytesUnchanged()
        {
            WriteHex(PinnedSettings(Mode.Graph), BuildPinnedGraph()).Should().Be(ExpectedGraphBytes);
        }
    }
}
