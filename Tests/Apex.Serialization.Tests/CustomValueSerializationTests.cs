using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Apex.Serialization.Extensions;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class CustomValueSerializationTests
    {
        public struct Point
        {
            public int X;
            public int Y;
        }

        public struct Outer
        {
            public int A;
            public Point P;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct ExplicitPoint
        {
            [FieldOffset(0)] public int X;
            [FieldOffset(4)] public int Y;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct ExplicitWithPoint
        {
            [FieldOffset(0)] public int A;
            [FieldOffset(4)] public Point P;
        }

        public struct Marker
        {
        }

        [InlineArray(2)]
        public struct TwoPoints
        {
            public Point Element;
        }

        [StructLayout(LayoutKind.Sequential, Size = 16)]
        public struct SizedBuffer
        {
            public Point First;
        }

        public sealed class Label
        {
            public string? Text;
        }

        public struct Labeled
        {
            public int X;
            public Label? Label;
        }

        public struct OrderProbe<TTag>
        {
            public int X;
        }

        public sealed class TagCustomFirst
        {
        }

        public sealed class TagPlainFirst
        {
        }

        public sealed class Holder<T>
        {
            public string? Name;
            public T? Value;
            public int Number;
        }

        public sealed class NullableHolder
        {
            public Point? Value;
        }

        public sealed class CustomThenLabel
        {
            public Point Custom;
            public Label? Next;
        }

        public sealed class SharedThenLabeled
        {
            public Label? Shared;
            public Labeled Value;
        }

        public sealed class LabeledThenShared
        {
            public Labeled First;
            public Label? Second;
        }

        private sealed class Calls
        {
            public int Writes;
            public int Reads;
        }

        // The bytes plain settings write for the order probe's holder, which match what the serializer wrote before value
        // types could be custom serialized, so a diff here means another instance's registration leaked into these settings.
        private const string PlainOrderProbeBytes = "010700000005000000010000006E00";

        private static int _tripleWrites;
        private static int _tripleReads;
        private static int _nullableWrites;
        private static int _nullableReads;

        private static readonly Point P1 = new Point { X = 1, Y = 2 };
        private static readonly Point P2 = new Point { X = 3, Y = 4 };
        private static readonly Point P3 = new Point { X = 5, Y = 6 };

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void TopLevelValueUsesItsCustomSerializer(Mode mode)
        {
            ShouldRoundTripThroughCustomSerializer(mode, P1, (a, b) => b.Should().Be(a), customValues: 1);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ClassFieldUsesItsCustomSerializer(Mode mode)
        {
            ShouldRoundTripThroughCustomSerializer(mode, new Holder<Point> { Name = "n", Value = P1, Number = 7 },
                (a, b) => b.Should().BeEquivalentTo(a), customValues: 1);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void FieldOfABlockCopyableStructUsesItsCustomSerializer(Mode mode)
        {
            ShouldRoundTripThroughCustomSerializer(mode, new Holder<Outer> { Value = new Outer { A = 9, P = P1 } },
                (a, b) => b.Value.Should().Be(a.Value), customValues: 1);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ArrayElementsUseTheirCustomSerializer(Mode mode)
        {
            ShouldRoundTripThroughCustomSerializer(mode, new[] { P1, P2, P3 }, (a, b) => b.Should().Equal(a), customValues: 3);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void TwoDimensionalArrayElementsUseTheirCustomSerializer(Mode mode)
        {
            ShouldRoundTripThroughCustomSerializer(mode, new[,] { { P1, P2 }, { P3, P1 } }, (a, b) => b.Should().BeEquivalentTo(a), customValues: 4);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ThreeDimensionalArrayElementsUseTheirCustomSerializer(Mode mode)
        {
            var values = new Point[2, 1, 2];
            values[0, 0, 0] = P1;
            values[1, 0, 1] = P2;

            ShouldRoundTripThroughCustomSerializer(mode, values, (a, b) => b.Should().BeEquivalentTo(a), customValues: 4);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ListElementsUseTheirCustomSerializer(Mode mode)
        {
            var list = new List<Point>(3) { P1, P2, P3 };

            ShouldRoundTripThroughCustomSerializer(mode, list, (a, b) => b.Should().Equal(a), customValues: list.Capacity);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void NullableValueUsesItsCustomSerializer(Mode mode)
        {
            ShouldRoundTripThroughCustomSerializer(mode, new NullableHolder { Value = P1 }, (a, b) => b.Value.Should().Be(a.Value), customValues: 1);
            ShouldRoundTripThroughCustomSerializer(mode, new NullableHolder(), (a, b) => b.Value.Should().BeNull(), customValues: 0);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ExplicitLayoutValueUsesItsCustomSerializer(Mode mode)
        {
            var calls = new Calls();
            var settings = Configure(mode)
                .RegisterCustomSerializer<ExplicitPoint>((p, w) => { calls.Writes++; w.Write(p.X); w.Write(p.Y); }, (_, _) => { })
                .RegisterCustomInstantiator<ExplicitPoint>(r => { calls.Reads++; return new ExplicitPoint { X = r.Read<int>(), Y = r.Read<int>() }; });
            var value = new Holder<ExplicitPoint> { Value = new ExplicitPoint { X = 1, Y = 2 } };

            WriteThenRead(settings, value).Value.Should().Be(value.Value);
            (calls.Writes, calls.Reads).Should().Be((1, 1));
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void FieldOfAnExplicitLayoutStructUsesItsCustomSerializer(Mode mode)
        {
            ShouldRoundTripThroughCustomSerializer(mode, new Holder<ExplicitWithPoint> { Value = new ExplicitWithPoint { A = 9, P = P1 } },
                (a, b) => b.Value.Should().Be(a.Value), customValues: 1);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void FieldlessValueUsesItsCustomSerializer(Mode mode)
        {
            var calls = new Calls();
            var settings = Configure(mode)
                .RegisterCustomSerializer<Marker>((_, w) => { calls.Writes++; w.Write((byte)0x4D); }, (_, _) => { })
                .RegisterCustomInstantiator<Marker>(r => { calls.Reads++; r.Read<byte>().Should().Be(0x4D); return new Marker(); });

            WriteThenRead(settings, new Holder<Marker> { Number = 7 }).Number.Should().Be(7);
            (calls.Writes, calls.Reads).Should().Be((1, 1));
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void ClosedNullableRegistrationWritesTheWholeNullable(Mode mode)
        {
            var calls = new Calls();
            var settings = Configure(mode)
                .RegisterCustomSerializer<Point?>((v, w) =>
                {
                    calls.Writes++;
                    w.Write(v.HasValue);
                    w.Write(v.GetValueOrDefault().X);
                    w.Write(v.GetValueOrDefault().Y);
                }, (_, _) => { })
                .RegisterCustomInstantiator<Point?>(r =>
                {
                    calls.Reads++;
                    var hasValue = r.Read<bool>();
                    var point = new Point { X = r.Read<int>(), Y = r.Read<int>() };
                    return hasValue ? point : null;
                });

            WriteThenRead(settings, new NullableHolder { Value = P1 }).Value.Should().Be(P1);
            WriteThenRead(settings, new NullableHolder()).Value.Should().BeNull();
            (calls.Writes, calls.Reads).Should().Be((2, 2));
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void OpenNullableRegistrationAppliesToEveryNullable(Mode mode)
        {
            _nullableWrites = 0;
            _nullableReads = 0;
            var settings = Configure(mode)
                .RegisterCustomSerializer(typeof(Nullable<>), typeof(CustomValueSerializationTests), nameof(WriteNullable), nameof(ReadNullable))
                .RegisterCustomInstantiator(typeof(Nullable<>), typeof(CustomValueSerializationTests), nameof(CreateNullable));

            WriteThenRead(settings, new NullableHolder { Value = P1 }).Value.Should().Be(P1);
            WriteThenRead(settings, new Holder<int?> { Value = 5 }).Value.Should().Be(5);
            WriteThenRead(settings, new Holder<int?>()).Value.Should().BeNull();
            (_nullableWrites, _nullableReads).Should().Be((3, 3));
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void OpenGenericRegistrationAppliesToClosedTuples(Mode mode)
        {
            _tripleWrites = 0;
            _tripleReads = 0;
            var settings = Configure(mode)
                .RegisterCustomSerializer(typeof(ValueTuple<,,>), typeof(CustomValueSerializationTests), nameof(WriteTriple), nameof(ReadTriple))
                .RegisterCustomInstantiator(typeof(ValueTuple<,,>), typeof(CustomValueSerializationTests), nameof(CreateTriple));

            WriteThenRead(settings, (1, 2, 3)).Should().Be((1, 2, 3));
            WriteThenRead(settings, new Holder<(int, int, int)> { Value = (4, 5, 6) }).Value.Should().Be((4, 5, 6));
            (_tripleWrites, _tripleReads).Should().Be((2, 2));
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void WithoutAnInstantiatorTheValueReadsBackAsDefaultAndTheStreamStaysInStep(Mode mode)
        {
            var readActions = 0;
            var settings = Configure(mode)
                .RegisterCustomSerializer<Point>((p, w) => { w.Write(p.X); w.Write(p.Y); }, (_, r) =>
                {
                    readActions++;
                    r.Read<int>();
                    r.Read<int>();
                });

            var loaded = WriteThenRead(settings, new CustomThenLabel { Custom = P1, Next = new Label { Text = "next" } });

            loaded.Custom.Should().Be(default(Point));
            loaded.Next.Text.Should().Be("next");
            readActions.Should().Be(1);
        }

        [Theory]
        [InlineData(Mode.Tree)]
        [InlineData(Mode.Graph)]
        public void PrecompiledMethodsUseTheCustomSerializer(Mode mode)
        {
            var calls = new Calls();
            var value = new Holder<Point> { Value = P1 };
            using var binary = Binary.Create(PointSettings(mode, calls));
            binary.Precompile(typeof(Holder<Point>));
            binary.Precompile<Point>();

            WriteThenRead(binary, value).Value.Should().Be(P1);
            WriteThenRead(binary, P2).Should().Be(P2);
            (calls.Writes, calls.Reads).Should().Be((2, 2));
        }

        [Fact]
        public void ValueTypeInstantiatorCanReadReferencesInGraphMode()
        {
            var settings = Configure(Mode.Graph)
                .RegisterCustomSerializer<Labeled>((v, w) => { w.Write(v.X); w.WriteObject(v.Label); }, (_, _) => { })
                .RegisterCustomInstantiator<Labeled>(r => new Labeled { X = r.Read<int>(), Label = r.ReadObject<Label>() });
            var label = new Label { Text = "shared" };

            var sharedFirst = WriteThenRead(settings, new SharedThenLabeled { Shared = label, Value = new Labeled { X = 1, Label = label } });
            var labeledFirst = WriteThenRead(settings, new LabeledThenShared { First = new Labeled { X = 2, Label = label }, Second = label });

            sharedFirst.Value.X.Should().Be(1);
            sharedFirst.Value.Label.Should().BeSameAs(sharedFirst.Shared);
            sharedFirst.Shared.Text.Should().Be("shared");
            labeledFirst.First.X.Should().Be(2);
            labeledFirst.First.Label.Should().BeSameAs(labeledFirst.Second);
            labeledFirst.Second.Text.Should().Be("shared");
        }

        [Fact]
        public void RegistrationAppliesOnlyToTheSettingsThatMakeItWhenCustomSettingsComeFirst()
        {
            ShouldFollowEachInstancesOwnSettings<TagCustomFirst>(customFirst: true);
        }

        [Fact]
        public void RegistrationAppliesOnlyToTheSettingsThatMakeItWhenPlainSettingsComeFirst()
        {
            ShouldFollowEachInstancesOwnSettings<TagPlainFirst>(customFirst: false);
        }

        [Fact]
        public void SettingsDifferingOnlyInInstantiatorsKeepTheirOwn()
        {
            Action<Point, IBinaryWriter> write = (p, w) => { w.Write(p.X); w.Write(p.Y); };
            Action<Point, IBinaryReader> read = (_, _) => { };
            var first = Configure(Mode.Tree).RegisterCustomSerializer(write, read)
                .RegisterCustomInstantiator<Point>(r => new Point { X = r.Read<int>(), Y = r.Read<int>() });
            var second = Configure(Mode.Tree).RegisterCustomSerializer(write, read)
                .RegisterCustomInstantiator<Point>(r => new Point { X = r.Read<int>() * 10, Y = r.Read<int>() * 10 });

            WriteThenRead(first, P1).Should().Be(P1);
            WriteThenRead(second, P1).Should().Be(new Point { X = 10, Y = 20 });
        }

        [Fact]
        public void InlineBufferHoldingACustomSerializedValueIsRejected()
        {
            var inlineArray = () => WriteThenRead(PointSettings(Mode.Tree, new Calls()), new Holder<TwoPoints>());
            var declaredSize = () => WriteThenRead(PointSettings(Mode.Tree, new Calls()), new Holder<SizedBuffer>());

            inlineArray.Should().Throw<NotSupportedException>();
            declaredSize.Should().Throw<NotSupportedException>();
        }

        private static void ShouldRoundTripThroughCustomSerializer<T>(Mode mode, T value, Action<T, T> assertEquivalent, int customValues)
        {
            var calls = new Calls();

            var loaded = WriteThenRead(PointSettings(mode, calls), value);

            assertEquivalent(value, loaded);
            (calls.Writes, calls.Reads).Should().Be((customValues, customValues));
        }

        private static void ShouldFollowEachInstancesOwnSettings<TTag>(bool customFirst)
        {
            var calls = new Calls();
            var value = new Holder<OrderProbe<TTag>> { Name = "n", Value = new OrderProbe<TTag> { X = 5 }, Number = 7 };
            var custom = Configure(Mode.Tree)
                .RegisterCustomSerializer<OrderProbe<TTag>>((p, w) => { calls.Writes++; w.Write(p.X + 1000); }, (_, _) => { })
                .RegisterCustomInstantiator<OrderProbe<TTag>>(r => { calls.Reads++; return new OrderProbe<TTag> { X = r.Read<int>() - 1000 }; });
            custom.UseSerializedVersionId = false;
            var plain = Configure(Mode.Tree);
            plain.UseSerializedVersionId = false;

            if (customFirst)
            {
                WriteThenRead(custom, value).Should().BeEquivalentTo(value);
            }

            Convert.ToHexString(Write(plain, value)).Should().Be(PlainOrderProbeBytes);
            WriteThenRead(plain, value).Should().BeEquivalentTo(value);

            if (!customFirst)
            {
                WriteThenRead(custom, value).Should().BeEquivalentTo(value);
            }

            (calls.Writes, calls.Reads).Should().Be((1, 1));
            Convert.ToHexString(Write(plain, value)).Should().Be(PlainOrderProbeBytes);
            Convert.ToHexString(Write(custom, value)).Should().NotBe(PlainOrderProbeBytes);
        }

        private static Settings Configure(Mode mode)
        {
            return new Settings { SerializationMode = mode, SupportSerializationHooks = true }.MarkSerializable(_ => true);
        }

        private static Settings PointSettings(Mode mode, Calls calls)
        {
            return Configure(mode)
                .RegisterCustomSerializer<Point>((p, w) => { calls.Writes++; w.Write(p.X); w.Write(p.Y); }, (_, _) => { })
                .RegisterCustomInstantiator<Point>(r => { calls.Reads++; return new Point { X = r.Read<int>(), Y = r.Read<int>() }; });
        }

        private static byte[] Write<T>(Settings settings, T value)
        {
            using var binary = Binary.Create(settings);
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }

        private static T WriteThenRead<T>(Settings settings, T value)
        {
            using var binary = Binary.Create(settings);
            return WriteThenRead(binary, value);
        }

        private static T WriteThenRead<T>(IBinary binary, T value)
        {
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            stream.Position = 0;
            return binary.Read<T>(stream);
        }

        private static void WriteTriple<A, B, C>(ValueTuple<A, B, C> value, IBinaryWriter writer)
        {
            ++_tripleWrites;
            writer.WriteObject(value.Item1);
            writer.WriteObject(value.Item2);
            writer.WriteObject(value.Item3);
        }

        private static void ReadTriple<A, B, C>(ValueTuple<A, B, C> value, IBinaryReader reader)
        {
        }

        private static ValueTuple<A, B, C> CreateTriple<A, B, C>(IBinaryReader reader)
        {
            ++_tripleReads;
            return (reader.ReadObject<A>(), reader.ReadObject<B>(), reader.ReadObject<C>());
        }

        private static void WriteNullable<T>(Nullable<T> value, IBinaryWriter writer)
            where T : struct
        {
            ++_nullableWrites;
            writer.Write(value.HasValue);
            if (value.HasValue)
            {
                writer.WriteObject(value.Value);
            }
        }

        private static void ReadNullable<T>(Nullable<T> value, IBinaryReader reader)
            where T : struct
        {
        }

        private static Nullable<T> CreateNullable<T>(IBinaryReader reader)
            where T : struct
        {
            ++_nullableReads;
            return reader.Read<bool>() ? reader.ReadObject<T>() : null;
        }
    }
}
