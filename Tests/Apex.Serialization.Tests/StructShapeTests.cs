using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class StructShapeTests : AbstractSerializerTestBase
    {
        [StructLayout(LayoutKind.Sequential, Size = 16)]
        public struct FieldlessBlob
        {
        }

        public struct Empty
        {
        }

        public sealed class BlobHolder
        {
            public FieldlessBlob Blob;
            public int After;
        }

        public struct SkipsInt
        {
            public int A;
            [NonSerialized] public int Skipped;
        }

        public struct SkipsString
        {
            public int A;
            [NonSerialized] public string? Skipped;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct ExplicitSkipsString
        {
            [FieldOffset(0)] public int A;
            [FieldOffset(8)] [NonSerialized] public string? Skipped;
        }

        public struct HoldsSkipsInt
        {
            public long L;
            public SkipsInt Inner;
        }

        public sealed class SkipHolder
        {
            public SkipsInt Value;
            public SkipsInt? Maybe;
            public SkipsInt[]? Values;
            public SkipsString WithString;
            public ExplicitSkipsString Explicit;
            public HoldsSkipsInt Nested;
            public int After;
        }

        [InlineArray(3)]
        public struct ThreeStrings
        {
            public string? Element;
        }

        public sealed class Node
        {
            public int Value;
        }

        [InlineArray(3)]
        public struct ThreeNodes
        {
            public Node? Element;
        }

        public sealed class NodeBufferHolder
        {
            public ThreeNodes Buffer;
            public Node? Other;
        }

        public sealed class StringBufferHolder
        {
            public ThreeStrings Buffer;
            public ThreeStrings? Maybe;
            public ThreeStrings[]? Buffers;
            public int After;
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        public struct SizedWithReference
        {
            [FieldOffset(0)] public string? Name;
            [FieldOffset(8)] public int X;
        }

        public sealed class SizedHolder
        {
            public SizedWithReference Value;
            public SizedWithReference? Maybe;
            public SizedWithReference? Missing;
            public SizedWithReference[]? Values;
            public int After;
        }

        [StructLayout(LayoutKind.Sequential, Size = 16)]
        public struct SizedSkipsInt
        {
            public int A;
            [NonSerialized] public int Skipped;
        }

        [StructLayout(LayoutKind.Sequential, Size = 16)]
        public struct CoincidentSizeWithReference
        {
            public int X;
            public string? Name;
        }

        [StructLayout(LayoutKind.Sequential, Size = 32)]
        public struct IgnoredSizeWithReference
        {
            public int X;
            public string? Name;
        }

        public struct UnsizedWithReference
        {
            public int X;
            public string? Name;
        }

        private const int SizedDataStart = 12;

        private const int SizedSkipsIntDataStart = 8;

        [Fact]
        public void FieldlessStructWithADeclaredSizeRoundTripsItsBytes()
        {
            var blob = PatternedBlob(0x10);

            RoundTrip(blob, (original, loaded) => BytesOf(loaded).Should().Equal(BytesOf(original)));
        }

        [Fact]
        public void FieldlessStructWithADeclaredSizeRoundTripsAsAClassField()
        {
            var holder = new BlobHolder { Blob = PatternedBlob(0x20), After = 7 };

            RoundTrip(holder, (original, loaded) =>
            {
                BytesOf(loaded.Blob).Should().Equal(BytesOf(original.Blob));
                loaded.After.Should().Be(7);
            });
        }

        [Fact]
        public void ArrayOfFieldlessStructsWithADeclaredSizeRoundTripsEveryByte()
        {
            var blobs = new[] { PatternedBlob(0x30), PatternedBlob(0x40), PatternedBlob(0x50) };

            RoundTrip(blobs, (original, loaded) =>
            {
                loaded.Should().HaveCount(3);
                for (var i = 0; i < 3; ++i)
                {
                    BytesOf(loaded[i]).Should().Equal(BytesOf(original[i]));
                }
            });
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ArrayOfFieldlessStructsWritesEachValuesSize(Mode mode)
        {
            (Write(mode, new FieldlessBlob[3]).Length - Write(mode, new FieldlessBlob[0]).Length).Should().Be(3 * 16);
            (Write(mode, new Empty[3]).Length - Write(mode, new Empty[0]).Length).Should().Be(3);
        }

        [Fact]
        public void StructSkippingAFieldReadsItBackAsDefault()
        {
            var holder = new SkipHolder
            {
                Value = new SkipsInt { A = 1, Skipped = 2 },
                Maybe = new SkipsInt { A = 3, Skipped = 4 },
                Values = new[] { new SkipsInt { A = 5, Skipped = 6 }, new SkipsInt { A = 7, Skipped = 8 } },
                WithString = new SkipsString { A = 9, Skipped = "skipped" },
                Explicit = new ExplicitSkipsString { A = 10, Skipped = "skipped" },
                Nested = new HoldsSkipsInt { L = 11, Inner = new SkipsInt { A = 12, Skipped = 13 } },
                After = 14,
            };

            RoundTrip(holder, (_, loaded) =>
            {
                loaded.Value.Should().Be(new SkipsInt { A = 1 });
                loaded.Maybe.Should().Be(new SkipsInt { A = 3 });
                loaded.Values.Should().Equal(new SkipsInt { A = 5 }, new SkipsInt { A = 7 });
                loaded.WithString.Should().Be(new SkipsString { A = 9 });
                loaded.Explicit.A.Should().Be(10);
                loaded.Explicit.Skipped.Should().BeNull();
                loaded.Nested.Should().Be(new HoldsSkipsInt { L = 11, Inner = new SkipsInt { A = 12 } });
                loaded.After.Should().Be(14);
            });
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void StructsDifferingOnlyInASkippedFieldWriteEqualBytes(Mode mode)
        {
            Convert.ToHexString(Write(mode, new SkipsInt { A = 1, Skipped = 2 }))
                .Should().Be(Convert.ToHexString(Write(mode, new SkipsInt { A = 1, Skipped = 3 })));
        }

        [Fact]
        public void TopLevelStructSkippingAFieldReadsItBackAsDefault()
        {
            RoundTrip(new SkipsInt { A = 1, Skipped = 2 }, (_, loaded) => loaded.Should().Be(new SkipsInt { A = 1 }));
        }

        [Fact]
        public void StructSkippingAFieldNeedsNoMarking()
        {
            using var binary = Binary.Create(new Settings());
            using var stream = new MemoryStream();

            binary.Write(new HoldsSkipsInt { L = 1, Inner = new SkipsInt { A = 2, Skipped = 3 } }, stream);
            stream.Position = 0;

            binary.Read<HoldsSkipsInt>(stream).Should().Be(new HoldsSkipsInt { L = 1, Inner = new SkipsInt { A = 2 } });
        }

        [Fact]
        public void InlineArrayOfReferencesRoundTripsEveryElement()
        {
            var holder = new StringBufferHolder { Buffer = Strings("a", "b", "c"), Maybe = Strings("d", null, "f"), Buffers = new[] { Strings("g", "h", "i"), Strings(null, "k", "l") }, After = 7 };

            RoundTrip(holder, (original, loaded) =>
            {
                ElementsOf(loaded.Buffer).Should().Equal(ElementsOf(original.Buffer));
                ElementsOf(loaded.Maybe!.Value).Should().Equal(ElementsOf(original.Maybe!.Value));
                loaded.Buffers.Should().HaveCount(2);
                ElementsOf(loaded.Buffers![0]).Should().Equal(ElementsOf(original.Buffers![0]));
                ElementsOf(loaded.Buffers[1]).Should().Equal(ElementsOf(original.Buffers[1]));
                loaded.After.Should().Be(7);
            });
        }

        [Fact]
        public void TopLevelInlineArrayOfReferencesRoundTripsEveryElement()
        {
            RoundTrip(Strings("a", "b", "c"), (original, loaded) => ElementsOf(loaded).Should().Equal(ElementsOf(original)));
        }

        [Fact]
        public void InlineArrayElementsKeepSharedReferencesInGraphMode()
        {
            var loaded = WriteThenRead(Mode.Graph, SharedNodeBuffer());

            ShouldHoldNodeValues(loaded);
            loaded.Buffer[0].Should().BeSameAs(loaded.Other);
            loaded.Buffer[2].Should().BeSameAs(loaded.Other);
        }

        [Fact]
        public void InlineArrayElementsReadASharedReferenceAsCopiesInTreeMode()
        {
            var loaded = WriteThenRead(Mode.Tree, SharedNodeBuffer());

            ShouldHoldNodeValues(loaded);
            loaded.Buffer[0].Should().NotBeSameAs(loaded.Buffer[2]);
            loaded.Buffer[0].Should().NotBeSameAs(loaded.Other);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void NullableValuesPrecompileAndRoundTrip(Mode mode)
        {
            using var binary = Binary.Create(Configure(mode));

            binary.Precompile(typeof(int?));
            binary.Precompile<(int, long)?>();

            WriteThenRead(binary, (int?)5).Should().Be(5);
            WriteThenRead(binary, (int?)null).Should().BeNull();
            WriteThenRead(binary, ((int, long)?)(1, 2L)).Should().Be((1, 2L));
        }

        [Fact]
        public void DeclaredSizeStructHoldingAReferenceKeepsTheBytesPastItsFields()
        {
            var holder = new SizedHolder { Value = Sized("first", 1, 0x60), Maybe = Sized("maybe", 5, 0xB0), Values = new[] { Sized("second", 2, 0x70), Sized(null, 3, 0x80) }, After = 7 };

            RoundTrip(holder, (original, loaded) =>
            {
                ShouldMatch(loaded.Value, original.Value);
                ShouldMatch(loaded.Maybe!.Value, original.Maybe!.Value);
                loaded.Missing.Should().BeNull();
                loaded.Values.Should().HaveCount(2);
                ShouldMatch(loaded.Values![0], original.Values![0]);
                ShouldMatch(loaded.Values[1], original.Values[1]);
                loaded.After.Should().Be(7);
            });
        }

        [Fact]
        public void TopLevelDeclaredSizeStructHoldingAReferenceKeepsTheBytesPastItsFields()
        {
            RoundTrip(Sized("top", 4, 0x90), (original, loaded) => ShouldMatch(loaded, original));
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void DeclaredSizeStructSkippingAFieldWritesItsTailButNotTheSkippedField(Mode mode)
        {
            Convert.ToHexString(Write(mode, SizedSkipping(skipped: 1, seed: 0xC0)))
                .Should().Be(Convert.ToHexString(Write(mode, SizedSkipping(skipped: 2, seed: 0xC0))));

            var loaded = WriteThenRead(mode, SizedSkipping(skipped: 3, seed: 0xD0));

            loaded.A.Should().Be(7);
            loaded.Skipped.Should().Be(0);
            BytesOf(loaded)[SizedSkipsIntDataStart..].Should().Equal(BytesOf(SizedSkipping(skipped: 3, seed: 0xD0))[SizedSkipsIntDataStart..]);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void SequentialStructHoldingAReferenceWritesNoPaddingWhenItsLayoutMatchesTheDeclaredSize(Mode mode)
        {
            Unsafe.SizeOf<CoincidentSizeWithReference>().Should().Be(16);

            Convert.ToHexString(Write(mode, PaddedWithReference(0x11)))
                .Should().Be(Convert.ToHexString(Write(mode, PaddedWithReference(0x22))));

            var loaded = WriteThenRead(mode, PaddedWithReference(0x33));

            (loaded.X, loaded.Name).Should().Be((4, "n"));
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void SequentialStructHoldingAReferenceWritesNoBytesForTheSizeTheRuntimeIgnores(Mode mode)
        {
            Unsafe.SizeOf<IgnoredSizeWithReference>().Should().Be(Unsafe.SizeOf<UnsizedWithReference>());

            Convert.ToHexString(Write(mode, new IgnoredSizeWithReference { X = 1, Name = "n" }))
                .Should().Be(Convert.ToHexString(Write(mode, new UnsizedWithReference { X = 1, Name = "n" })));
        }

        private static NodeBufferHolder SharedNodeBuffer()
        {
            var shared = new Node { Value = 1 };
            var holder = new NodeBufferHolder { Other = shared };
            holder.Buffer[0] = shared;
            holder.Buffer[1] = new Node { Value = 2 };
            holder.Buffer[2] = shared;
            return holder;
        }

        private static void ShouldHoldNodeValues(NodeBufferHolder loaded)
        {
            (loaded.Buffer[0]!.Value, loaded.Buffer[1]!.Value, loaded.Buffer[2]!.Value, loaded.Other!.Value).Should().Be((1, 2, 1, 1));
        }

        private static FieldlessBlob PatternedBlob(byte seed)
        {
            var blob = new FieldlessBlob();
            Fill(ref blob, 0, Unsafe.SizeOf<FieldlessBlob>(), seed);
            return blob;
        }

        private static SizedWithReference Sized(string? name, int x, byte seed)
        {
            var value = new SizedWithReference { Name = name, X = x };
            Fill(ref value, SizedDataStart, Unsafe.SizeOf<SizedWithReference>() - SizedDataStart, seed);
            return value;
        }

        private static SizedSkipsInt SizedSkipping(int skipped, byte seed)
        {
            var value = new SizedSkipsInt { A = 7, Skipped = skipped };
            Fill(ref value, SizedSkipsIntDataStart, Unsafe.SizeOf<SizedSkipsInt>() - SizedSkipsIntDataStart, seed);
            return value;
        }

        private static CoincidentSizeWithReference PaddedWithReference(byte seed)
        {
            var value = new CoincidentSizeWithReference();
            var nameOffset = (int)Unsafe.ByteOffset(ref Unsafe.As<CoincidentSizeWithReference, byte>(ref value), ref Unsafe.As<string?, byte>(ref value.Name));
            Fill(ref value, 0, nameOffset, seed);
            Fill(ref value, nameOffset + IntPtr.Size, Unsafe.SizeOf<CoincidentSizeWithReference>() - nameOffset - IntPtr.Size, seed);
            value.X = 4;
            value.Name = "n";
            return value;
        }

        private static void ShouldMatch(SizedWithReference loaded, SizedWithReference original)
        {
            loaded.Name.Should().Be(original.Name);
            loaded.X.Should().Be(original.X);
            BytesOf(loaded)[SizedDataStart..].Should().Equal(BytesOf(original)[SizedDataStart..]);
        }

        private static ThreeStrings Strings(string? first, string? second, string? third)
        {
            var buffer = new ThreeStrings();
            buffer[0] = first;
            buffer[1] = second;
            buffer[2] = third;
            return buffer;
        }

        private static string?[] ElementsOf(ThreeStrings buffer) => new[] { buffer[0], buffer[1], buffer[2] };

        private static void Fill<T>(ref T value, int start, int length, byte seed)
        {
            var bytes = MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref value), Unsafe.SizeOf<T>()).Slice(start, length);
            for (var i = 0; i < bytes.Length; ++i)
            {
                bytes[i] = (byte)(seed + i);
            }
        }

        private static byte[] BytesOf<T>(T value)
        {
            return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref value), Unsafe.SizeOf<T>()).ToArray();
        }

        private static Settings Configure(Mode mode)
        {
            return new Settings { SerializationMode = mode, UseSerializedVersionId = false }.MarkSerializable(_ => true);
        }

        private static byte[] Write<T>(Mode mode, T value)
        {
            using var binary = Binary.Create(Configure(mode));
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }

        private static T WriteThenRead<T>(Mode mode, T value)
        {
            using var binary = Binary.Create(Configure(mode));
            return WriteThenRead(binary, value);
        }

        private static T WriteThenRead<T>(IBinary binary, T value)
        {
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            stream.Position = 0;
            return binary.Read<T>(stream);
        }
    }
}
