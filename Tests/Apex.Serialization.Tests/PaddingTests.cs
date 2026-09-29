using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Apex.Serialization.Tests
{
    public class PaddingTests : AbstractSerializerTestBase
    {
        public struct Padded
        {
            public byte A;
            public long B;
        }

        public struct Nested
        {
            public byte Flag;
            public Padded Inner;
        }

        public struct IntByteInt
        {
            public int A;
            public byte B;
            public int C;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct ExplicitGap
        {
            [FieldOffset(0)] public int A;
            [FieldOffset(8)] public byte B;
        }

        public sealed class FieldHolder
        {
            public Padded Value;
        }

        public sealed class NestedHolder
        {
            public Nested Value;
        }

        public sealed class NullableHolder
        {
            public Padded? Value;
        }

        public sealed class BoxedHolder
        {
            public object? Value;
        }

        public sealed class CustomWrittenHolder
        {
            public Padded Value;
        }

        public sealed class VectorHolder<T>
            where T : struct
        {
            public Vector<T> Value;
        }

        public unsafe struct FixedBytesThenInt
        {
            public fixed byte Bytes[5];
            public int X;
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

        public struct Dense
        {
            public int A;
            public float B;
            public long C;
        }

        public unsafe struct FixedBytes
        {
            public fixed byte Bytes[8];
        }

        [InlineArray(4)]
        public struct FourInts
        {
            public int Element;
        }

        public sealed class UnpaddedPin
        {
            public Dense Dense;
            public (int, int) Pair;
            public decimal Decimal;
            public Guid Guid;
            public DayOfWeek Day;
            public bool Flag;
            public char Letter;
            public Dense? MaybeDense;
            public Dense[]? DenseArray;
            public (int, int)[,]? PairGrid;
            public FixedBytes Fixed;
            public FourInts Inline;
        }

        private const byte FirstPoison = 0xAA;
        private const byte SecondPoison = 0x55;

        private static readonly byte[] PaddedBlock = [0x11, 0, 0, 0, 0, 0, 0, 0, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22];
        private static readonly byte[] NestedBlock = [0x33, 0, 0, 0, 0, 0, 0, 0, .. PaddedBlock];
        private static readonly byte[] IntByteIntBlock = [0x0D, 0x0C, 0x0B, 0x0A, 0x0E, 0, 0, 0, 0x04, 0x03, 0x02, 0x01];
        private static readonly byte[] PoisonedPaddedBlock = [0x11, FirstPoison, FirstPoison, FirstPoison, FirstPoison, FirstPoison, FirstPoison, FirstPoison, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22];

        // Captured before the writers zeroed padding, so a diff here means the output changed for values that have none.
        private const string UnpaddedTreeBytes =
            "01050000000000020000000000A302000000000000010000000000204003000000000000003031323334353637013C2D1E0F5A4B78"
            + "698796A5B4C3D2E1F0400000004100000042000000430000005A000107000000000008410900000000000000040000000500000001"
            + "020000000A000000000038410C000000000000000D000000000068410F00000000000000010200000002000000100000001100000012"
            + "0000001300000014000000150000001600000017000000";

        private const string UnpaddedGraphBytes =
            "01FFFFFFFF050000000000020000000000A302000000000000010000000000204003000000000000003031323334353637013C2D1E0F"
            + "5A4B78698796A5B4C3D2E1F0400000004100000042000000430000005A00010700000000000841090000000000000004000000050000"
            + "0001FFFFFFFF020000000A000000000038410C000000000000000D000000000068410F0000000000000001FFFFFFFF02000000020000"
            + "001000000011000000120000001300000014000000150000001600000017000000";

        private delegate void Fill<T>(ref T value);

        private unsafe delegate void BlockWrite(ref BufferedStream stream, void* source, uint byteLength);

        private unsafe delegate void ValueWrite(ref BufferedStream stream, Padded* value);

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void TopLevelStructKeepsFieldBytesAndZeroesPadding(Mode mode)
        {
            ShouldWriteExactly<Padded>(mode, FillPadded, PaddedBlock);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void TopLevelTupleKeepsFieldBytesAndZeroesPadding(Mode mode)
        {
            byte[] expected = [.. BitConverter.GetBytes(1f), .. BitConverter.GetBytes(2f), .. BitConverter.GetBytes(3f), 0, 0, 0, 0];

            ShouldWriteExactly(mode, (ref (float, float, float) t) =>
            {
                t.Item1 = 1;
                t.Item2 = 2;
                t.Item3 = 3;
            }, expected);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void StructHoldingANullableKeepsFieldBytesAndZeroesPadding(Mode mode)
        {
            ShouldWriteExactly(mode, (ref NullableThenByte value) =>
            {
                Unsafe.As<int?, bool>(ref value.N) = true;
                Unsafe.AsRef(in Nullable.GetValueRefOrDefaultRef(in value.N)) = 0x01020304;
                value.C = 0x0E;
            }, [0x01, 0, 0, 0, 0x04, 0x03, 0x02, 0x01, 0x0E, 0, 0, 0]);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ExplicitLayoutGapIsWrittenAsZeros(Mode mode)
        {
            ShouldWriteExactly(mode, (ref ExplicitGap value) =>
            {
                value.A = 0x0A0B0C0D;
                value.B = 0x0E;
            }, [0x0D, 0x0C, 0x0B, 0x0A, 0, 0, 0, 0, 0x0E, 0, 0, 0]);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ClassFieldPaddingIsWrittenAsZeros(Mode mode)
        {
            ShouldWriteZeroPadding(mode, pattern =>
            {
                var holder = new FieldHolder();
                Poison(ref holder.Value, pattern, FillPadded);
                return holder;
            }, PaddedBlock, count: 1);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void NestedStructPaddingIsWrittenAsZeros(Mode mode)
        {
            ShouldWriteZeroPadding(mode, pattern =>
            {
                var holder = new NestedHolder();
                Poison(ref holder.Value, pattern, FillNested);
                return holder;
            }, NestedBlock, count: 1);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ArrayPaddingIsWrittenAsZeros(Mode mode)
        {
            ShouldWriteZeroPadding(mode, pattern => PoisonedArray<Padded>(70_000, pattern, FillPadded), PaddedBlock, count: 70_000);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void TwoDimensionalArrayPaddingIsWrittenAsZeros(Mode mode)
        {
            ShouldWriteZeroPadding(mode, pattern =>
            {
                var values = new Padded[3, 4];
                for (var i = 0; i < 3; ++i)
                {
                    for (var j = 0; j < 4; ++j)
                    {
                        Poison(ref values[i, j], pattern, FillPadded);
                    }
                }

                return values;
            }, PaddedBlock, count: 12);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ThreeDimensionalArrayPaddingIsWrittenAsZeros(Mode mode)
        {
            ShouldWriteZeroPadding(mode, pattern =>
            {
                var values = new Padded[2, 3, 4];
                for (var i = 0; i < 2; ++i)
                {
                    for (var j = 0; j < 3; ++j)
                    {
                        for (var k = 0; k < 4; ++k)
                        {
                            Poison(ref values[i, j, k], pattern, FillPadded);
                        }
                    }
                }

                return values;
            }, PaddedBlock, count: 24);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ListPaddingIsWrittenAsZeros(Mode mode)
        {
            ShouldWriteZeroPadding(mode, pattern =>
            {
                var list = new List<Padded>(new Padded[5]);
                var items = CollectionsMarshal.AsSpan(list);
                for (var i = 0; i < items.Length; ++i)
                {
                    Poison(ref items[i], pattern, FillPadded);
                }

                return list;
            }, PaddedBlock, count: 5);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void NullableFieldPaddingIsWrittenAsZeros(Mode mode)
        {
            ShouldWriteZeroPadding(mode, pattern =>
            {
                var holder = new NullableHolder();
                Poison(ref holder.Value, pattern, FillNullablePadded);
                return holder;
            }, PaddedBlock, count: 1);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void BoxedStructPaddingIsWrittenAsZeros(Mode mode)
        {
            ShouldWriteZeroPadding(mode, pattern =>
            {
                var box = (object)new Padded();
                Poison(ref Unsafe.Unbox<Padded>(box), pattern, FillPadded);
                return new BoxedHolder { Value = box };
            }, PaddedBlock, count: 1);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void CustomSerializerWritePaddingIsWrittenAsZeros(Mode mode)
        {
            ShouldWriteZeroPadding(mode, pattern =>
            {
                var holder = new CustomWrittenHolder();
                Poison(ref holder.Value, pattern, FillPadded);
                return holder;
            }, PaddedBlock, count: 1, settings =>
            {
                settings.SupportSerializationHooks = true;
                settings.RegisterCustomSerializer<CustomWrittenHolder>(
                    (holder, writer) => writer.Write(holder.Value),
                    (holder, reader) => holder.Value = reader.Read<Padded>());
            });
        }

        [Theory]
        [InlineData(Mode.Tree, UnpaddedTreeBytes)]
        [InlineData(Mode.Graph, UnpaddedGraphBytes)]
        public void UnpaddedValuesWriteBytesUnchanged(Mode mode, string expectedHex)
        {
            Convert.ToHexString(Write(mode, BuildUnpaddedPin())).Should().Be(expectedHex);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void RuntimeSizedVectorKeepsEveryLane(Mode mode)
        {
            ShouldKeepEveryLane(mode, new Vector<float>(Lanes(i => i + 0.5f)));
            ShouldKeepEveryLane(mode, new Vector<int>(Lanes(i => i + 1)));
        }

        [Fact]
        public void PaddedValuesRoundTrip()
        {
            var padded = new Padded { A = 1, B = 2 };

            RoundTrip(padded);
            RoundTrip(new Nested { Flag = 3, Inner = padded });
            RoundTrip((1f, 2f, 3f));
            RoundTrip(new[] { padded, new Padded { A = 4, B = 5 } });
            RoundTrip(new[,] { { padded }, { new Padded { A = 6, B = 7 } } });
            RoundTrip(new List<Padded> { padded, new Padded { A = 8, B = 9 } });
            RoundTrip(new NullableHolder { Value = padded });
            RoundTrip(new NullableHolder());
            RoundTrip(new NullableThenByte { N = 10, C = 11 });
            RoundTrip(new NullableThenByte { C = 12 });
        }

        [Fact]
        public unsafe void FixedBufferRoundTripsEveryElement()
        {
            var value = new FixedBytesThenInt { X = 42 };
            for (var i = 0; i < 5; ++i)
            {
                value.Bytes[i] = (byte)(i + 1);
            }

            RoundTrip(value, (_, loaded) =>
            {
                new ReadOnlySpan<byte>(loaded.Bytes, 5).ToArray().Should().Equal(1, 2, 3, 4, 5);
                loaded.X.Should().Be(42);
            });
        }

        [Fact]
        public void InlineArrayRoundTripsEveryElement()
        {
            var value = new ThreeFloatBoolPairs();
            value[0] = (1f, true);
            value[1] = (2f, false);
            value[2] = (3f, true);

            RoundTrip(value, (_, loaded) => new[] { loaded[0], loaded[1], loaded[2] }.Should().Equal((1f, true), (2f, false), (3f, true)));
        }

        [Fact]
        public unsafe void ValueWriteZeroesPaddingOverAStaleBuffer()
        {
            var output = WriteOverStaleBuffer((ref BufferedStream stream, Padded* value) => stream.Write(*value));

            output.Should().Equal(PaddedBlock);
        }

        [Fact]
        public unsafe void RawValueWriteCarriesSourcePaddingIntoTheOutput()
        {
            var output = WriteOverStaleBuffer((ref BufferedStream stream, Padded* value) => stream.WriteBytes(value, (uint)sizeof(Padded)));

            output.Should().Equal(PoisonedPaddedBlock);
        }

        [Fact]
        public unsafe void ArrayWriteZeroesPaddingSplitAcrossABufferFlush()
        {
            var values = PoisonedArray<Padded>(100_000, FirstPoison, FillPadded);

            var output = WriteAfterPrefix(values, flushAtByteOfValue: 4, (ref BufferedStream stream, void* source, uint byteLength) => stream.WriteValues<Padded>(source, byteLength));

            ShouldHoldEveryValue(output, PaddedBlock, values.Length);
        }

        [Fact]
        public unsafe void TwelveByteArrayWriteZeroesPaddingOfEveryValue()
        {
            var values = PoisonedArray<IntByteInt>(100_000, FirstPoison, FillIntByteInt);

            var output = WriteAfterPrefix(values, flushAtByteOfValue: 6, (ref BufferedStream stream, void* source, uint byteLength) => stream.WriteValues<IntByteInt>(source, byteLength));

            ShouldHoldEveryValue(output, IntByteIntBlock, values.Length);
        }

        [Fact]
        public unsafe void TwentyFourByteArrayWriteZeroesPaddingOfEveryValue()
        {
            var values = PoisonedArray<Nested>(50_000, FirstPoison, FillNested);

            var output = WriteAfterPrefix(values, flushAtByteOfValue: 11, (ref BufferedStream stream, void* source, uint byteLength) => stream.WriteValues<Nested>(source, byteLength));

            ShouldHoldEveryValue(output, NestedBlock, values.Length);
        }

        [Fact]
        public unsafe void RawArrayWriteCarriesSourcePaddingIntoTheOutput()
        {
            var values = PoisonedArray<Padded>(100_000, FirstPoison, FillPadded);

            var output = WriteAfterPrefix(values, flushAtByteOfValue: 4, (ref BufferedStream stream, void* source, uint byteLength) => stream.WriteBytes(source, byteLength));

            ShouldHoldEveryValue(output, PoisonedPaddedBlock, values.Length);
        }

        private static void ShouldWriteExactly<T>(Mode mode, Fill<T> fill, byte[] expected)
            where T : struct
        {
            foreach (var pattern in new[] { FirstPoison, SecondPoison })
            {
                var value = default(T);
                Poison(ref value, pattern, fill);

                Write(mode, value).Should().Equal(expected);
            }
        }

        private static void ShouldWriteZeroPadding<T>(Mode mode, Func<byte, T> poisoned, byte[] block, int count, Action<Settings>? configure = null)
        {
            var first = Write(mode, poisoned(FirstPoison), configure);
            var second = Write(mode, poisoned(SecondPoison), configure);

            first.Should().Equal(second);
            Occurrences(first, block).Should().Be(count);
        }

        private static void ShouldHoldEveryValue(byte[] output, byte[] block, int count)
        {
            output.Length.Should().Be(count * block.Length);
            Enumerable.Range(0, count)
                .Where(i => !output.AsSpan(i * block.Length, block.Length).SequenceEqual(block))
                .Should().BeEmpty();
        }

        private static void ShouldKeepEveryLane<T>(Mode mode, Vector<T> value)
            where T : struct
        {
            using var scope = new AssertionScope();
            WriteThenRead(mode, value).Should().Be(value);
            WriteThenRead(mode, new VectorHolder<T> { Value = value }).Value.Should().Be(value);
            WriteThenRead(mode, new[] { value, value }).Should().Equal(value, value);
        }

        private static T[] Lanes<T>(Func<int, T> lane)
            where T : struct
        {
            return Enumerable.Range(0, Vector<T>.Count).Select(lane).ToArray();
        }

        private static byte[] Write<T>(Mode mode, T value, Action<Settings>? configure = null)
        {
            var settings = WriteSettings(mode);
            configure?.Invoke(settings);
            using var binary = Binary.Create(settings);
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }

        private static T WriteThenRead<T>(Mode mode, T value)
        {
            using var binary = Binary.Create(WriteSettings(mode));
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            stream.Position = 0;
            return binary.Read<T>(stream);
        }

        private static Settings WriteSettings(Mode mode)
        {
            return new Settings { SerializationMode = mode, UseSerializedVersionId = false }.MarkSerializable(_ => true);
        }

        private static unsafe byte[] WriteAfterPrefix<T>(T[] values, int flushAtByteOfValue, BlockWrite write)
            where T : unmanaged
        {
            var prefixLength = (int)((BufferedStream.MaxSize - (uint)flushAtByteOfValue) % (uint)sizeof(T));

            using var memory = new MemoryStream();
            var stream = BufferedStream.Create();
            try
            {
                stream.WriteTo(memory);
                stream.ReserveSize(prefixLength);
                for (var i = 0; i < prefixLength; ++i)
                {
                    stream.Write((byte)i);
                }

                fixed (void* source = values)
                {
                    write(ref stream, source, (uint)(values.Length * sizeof(T)));
                }

                stream.Flush();
            }
            finally
            {
                stream.Dispose();
            }

            return memory.ToArray()[prefixLength..];
        }

        private static unsafe byte[] WriteOverStaleBuffer(ValueWrite write)
        {
            var value = new Padded();
            Poison(ref value, FirstPoison, FillPadded);
            var stale = new byte[64];
            Array.Fill(stale, SecondPoison);

            using var memory = new MemoryStream();
            var stream = BufferedStream.Create();
            try
            {
                stream.WriteTo(Stream.Null);
                fixed (byte* source = stale)
                {
                    stream.WriteBytes(source, (uint)stale.Length);
                }

                stream.WriteTo(memory);
                stream.ReserveSize(sizeof(Padded));
                write(ref stream, &value);
                stream.Flush();
            }
            finally
            {
                stream.Dispose();
            }

            return memory.ToArray();
        }

        private static void Poison<T>(ref T slot, byte pattern, Fill<T> fill)
        {
            Unsafe.InitBlockUnaligned(ref Unsafe.As<T, byte>(ref slot), pattern, (uint)Unsafe.SizeOf<T>());
            fill(ref slot);
        }

        private static T[] PoisonedArray<T>(int length, byte pattern, Fill<T> fill)
        {
            var values = new T[length];
            for (var i = 0; i < values.Length; ++i)
            {
                Poison(ref values[i], pattern, fill);
            }

            return values;
        }

        private static void FillPadded(ref Padded value)
        {
            value.A = 0x11;
            value.B = 0x2222222222222222;
        }

        private static void FillNested(ref Nested value)
        {
            value.Flag = 0x33;
            FillPadded(ref value.Inner);
        }

        private static void FillIntByteInt(ref IntByteInt value)
        {
            value.A = 0x0A0B0C0D;
            value.B = 0x0E;
            value.C = 0x01020304;
        }

        private static void FillNullablePadded(ref Padded? value)
        {
            Unsafe.As<Padded?, bool>(ref value) = true;
            FillPadded(ref Unsafe.AsRef(in Nullable.GetValueRefOrDefaultRef(in value)));
        }

        private static int Occurrences(byte[] output, byte[] block)
        {
            var count = 0;
            var remaining = output.AsSpan();
            int index;
            while ((index = remaining.IndexOf(block)) >= 0)
            {
                ++count;
                remaining = remaining[(index + 1)..];
            }

            return count;
        }

        private static unsafe UnpaddedPin BuildUnpaddedPin()
        {
            var pin = new UnpaddedPin
            {
                Dense = new Dense { A = 1, B = 2.5f, C = 3 },
                Pair = (4, 5),
                Decimal = 6.75m,
                Guid = new Guid("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0"),
                Day = DayOfWeek.Friday,
                Flag = true,
                Letter = 'Z',
                MaybeDense = new Dense { A = 7, B = 8.5f, C = 9 },
                DenseArray = new[] { new Dense { A = 10, B = 11.5f, C = 12 }, new Dense { A = 13, B = 14.5f, C = 15 } },
                PairGrid = new (int, int)[,] { { (16, 17), (18, 19) }, { (20, 21), (22, 23) } },
            };
            for (var i = 0; i < 8; ++i)
            {
                pin.Fixed.Bytes[i] = (byte)(0x30 + i);
            }

            for (var i = 0; i < 4; ++i)
            {
                pin.Inline[i] = 0x40 + i;
            }

            return pin;
        }
    }
}
