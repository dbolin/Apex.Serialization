using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Apex.Serialization.Internal;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class PaddingMapTests
    {
        public struct ByteThenLong
        {
            public byte A;
            public long B;
        }

        public struct BoolThenInt
        {
            public bool A;
            public int B;
        }

        public struct CharThenInt
        {
            public char A;
            public int B;
        }

        public struct FlagThenPadded
        {
            public byte Flag;
            public ByteThenLong Inner;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct PackedByOne
        {
            public byte A;
            public long B;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        public struct PackedByTwo
        {
            public byte A;
            public long B;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct Overlapping
        {
            [FieldOffset(0)] public long Wide;
            [FieldOffset(4)] public int Inside;
            [FieldOffset(6)] public int Straddling;
            [FieldOffset(12)] public byte Last;
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        public struct ExplicitWithDeclaredSize
        {
            [FieldOffset(0)] public int A;
            [FieldOffset(8)] public byte B;
        }

        [StructLayout(LayoutKind.Sequential, Size = 16)]
        public struct HandSizedBuffer
        {
            public byte First;
        }

        public unsafe struct FixedBytesThenInt
        {
            public fixed byte Bytes[5];
            public int X;
        }

        public unsafe struct FixedBytesOnly
        {
            public fixed byte Bits[32];
        }

        public unsafe struct FixedIntsThenByte
        {
            public fixed int Values[3];
            public byte Tail;
        }

        [InlineArray(4)]
        public struct FourInts
        {
            public int Element;
        }

        [InlineArray(3)]
        public struct ThreeFloatBoolPairs
        {
            public (float, bool) Element;
        }

        public struct NullableThenByte
        {
            public int? N;
            public byte C;
        }

        public enum ByteEnum : byte { A }
        public enum SByteEnum : sbyte { A }
        public enum ShortEnum : short { A }
        public enum UShortEnum : ushort { A }
        public enum IntEnum { A }
        public enum UIntEnum : uint { A }
        public enum LongEnum : long { A }
        public enum ULongEnum : ulong { A }

        public struct ByteEnumThenLong
        {
            public ByteEnum E;
            public long X;
        }

        public struct ShortEnumThenInt
        {
            public ShortEnum E;
            public int X;
        }

        public struct Empty
        {
        }

        public struct EmptyThenInt
        {
            public Empty E;
            public int X;
        }

        [StructLayout(LayoutKind.Sequential, Size = 16)]
        public struct FieldlessWithDeclaredSize
        {
        }

        public struct WithNonSerializedField
        {
            public byte A;
            [NonSerialized] public int Skipped;
        }

        [Theory]
        [InlineData(typeof(bool))]
        [InlineData(typeof(byte))]
        [InlineData(typeof(sbyte))]
        [InlineData(typeof(char))]
        [InlineData(typeof(decimal))]
        [InlineData(typeof(double))]
        [InlineData(typeof(float))]
        [InlineData(typeof(int))]
        [InlineData(typeof(uint))]
        [InlineData(typeof(long))]
        [InlineData(typeof(ulong))]
        [InlineData(typeof(short))]
        [InlineData(typeof(ushort))]
        [InlineData(typeof(IntPtr))]
        [InlineData(typeof(UIntPtr))]
        [InlineData(typeof(Guid))]
        public void SerializerPrimitivesHaveNoPadding(Type type)
        {
            PaddingMap.GetRanges(type).Should().BeEmpty();
        }

        [Theory]
        [InlineData(typeof(ByteEnum))]
        [InlineData(typeof(SByteEnum))]
        [InlineData(typeof(ShortEnum))]
        [InlineData(typeof(UShortEnum))]
        [InlineData(typeof(IntEnum))]
        [InlineData(typeof(UIntEnum))]
        [InlineData(typeof(LongEnum))]
        [InlineData(typeof(ULongEnum))]
        public void EnumsHaveNoPadding(Type type)
        {
            PaddingMap.GetRanges(type).Should().BeEmpty();
        }

        [Theory]
        [InlineData(typeof(Vector<float>))]
        [InlineData(typeof(Vector<int>))]
        [InlineData(typeof(Vector<byte>))]
        public void RuntimeSizedVectorHasNoPadding(Type type)
        {
            PaddingMap.GetRanges(type).Should().BeEmpty();
        }

        [Fact]
        public void EnumFieldCoversItsUnderlyingSize()
        {
            PaddingMap.GetRanges(typeof(ByteEnumThenLong)).Should().Equal((1, 7));
            PaddingMap.GetRanges(typeof(ShortEnumThenInt)).Should().Equal((2, 2));
        }

        [Fact]
        public void AlignmentGapAfterASmallFieldIsPadding()
        {
            PaddingMap.GetRanges(typeof(ByteThenLong)).Should().Equal((1, 7));
            PaddingMap.GetRanges(typeof(BoolThenInt)).Should().Equal((1, 3));
            PaddingMap.GetRanges(typeof(CharThenInt)).Should().Equal((2, 2));
        }

        [Fact]
        public void AutoLayoutTuplesUseTheRuntimeOffsets()
        {
            PaddingMap.GetRanges(typeof((float, float, float))).Should().Equal((12, 4));
            PaddingMap.GetRanges(typeof((float, bool))).Should().Equal((5, 3));
            PaddingMap.GetRanges(typeof((int, int))).Should().BeEmpty();
        }

        [Fact]
        public void NestedStructPaddingIsMappedAtItsOffsetInTheOuterStruct()
        {
            PaddingMap.GetRanges(typeof(FlagThenPadded)).Should().Equal((1, 7), (9, 7));
        }

        [Fact]
        public void PackChangesWhereFieldsSit()
        {
            PaddingMap.GetRanges(typeof(PackedByOne)).Should().BeEmpty();
            PaddingMap.GetRanges(typeof(PackedByTwo)).Should().Equal((1, 1));
        }

        [Fact]
        public void ByteCoveredByAnyOverlappingFieldIsNotPadding()
        {
            PaddingMap.GetRanges(typeof(Overlapping)).Should().Equal((10, 2), (13, 3));
        }

        [Fact]
        public void DeclaredSizeCountsAsFullyCovered()
        {
            PaddingMap.GetRanges(typeof(ExplicitWithDeclaredSize)).Should().BeEmpty();
            PaddingMap.GetRanges(typeof(HandSizedBuffer)).Should().BeEmpty();
        }

        [Fact]
        public void FixedBufferCountsAsFullyCovered()
        {
            PaddingMap.GetRanges(typeof(FixedBytesOnly)).Should().BeEmpty();
            PaddingMap.GetRanges(typeof(FixedBytesThenInt)).Should().Equal((5, 3));
            PaddingMap.GetRanges(typeof(FixedIntsThenByte)).Should().Equal((13, 3));
        }

        [Fact]
        public void InlineArrayRepeatsItsElementMap()
        {
            PaddingMap.GetRanges(typeof(FourInts)).Should().BeEmpty();
            PaddingMap.GetRanges(typeof(ThreeFloatBoolPairs)).Should().Equal((5, 3), (13, 3), (21, 3));
        }

        [Fact]
        public void NullableHasPaddingBetweenHasValueAndValue()
        {
            PaddingMap.GetRanges(typeof(int?)).Should().Equal((1, 3));
            PaddingMap.GetRanges(typeof(long?)).Should().Equal((1, 7));
            PaddingMap.GetRanges(typeof(ByteThenLong?)).Should().Equal((1, 7), (9, 7));
            PaddingMap.GetRanges(typeof(NullableThenByte)).Should().Equal((1, 3), (9, 3));
        }

        [Fact]
        public void EmptyStructIsAllPadding()
        {
            PaddingMap.GetRanges(typeof(Empty)).Should().Equal((0, 1));
            PaddingMap.GetRanges(typeof(ValueTuple)).Should().Equal((0, 1));
            PaddingMap.GetRanges(typeof(EmptyThenInt)).Should().Equal((0, 4));
        }

        [Fact]
        public void FieldlessStructDeclaredLargerThanOneByteIsOpaque()
        {
            PaddingMap.GetRanges(typeof(FieldlessWithDeclaredSize)).Should().BeEmpty();
        }

        [Fact]
        public void NonSerializedFieldIsNotPadding()
        {
            PaddingMap.GetRanges(typeof(WithNonSerializedField)).Should().Equal((1, 3));
        }

        [Fact]
        public void HasPaddingFollowsTheMap()
        {
            PaddingMap<int>.HasPadding.Should().BeFalse();
            PaddingMap<(int, int)>.HasPadding.Should().BeFalse();
            PaddingMap<ByteThenLong>.HasPadding.Should().BeTrue();
            PaddingMap<Empty>.HasPadding.Should().BeTrue();
        }
    }
}
