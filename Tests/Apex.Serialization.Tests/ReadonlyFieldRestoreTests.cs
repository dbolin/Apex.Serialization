using System.IO;
using System.Reflection;
using Apex.Serialization.Internal.Reflection;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class ReadonlyFieldRestoreTests
    {
        public sealed class WithReadonly
        {
            public readonly int Value;

            public WithReadonly()
            {
            }

            public WithReadonly(int value)
            {
                Value = value;
            }
        }

        public abstract class ReadonlyBase
        {
            public readonly int BaseValue;

            protected ReadonlyBase()
            {
            }

            protected ReadonlyBase(int value)
            {
                BaseValue = value;
            }
        }

        public sealed class ReadonlyDerived : ReadonlyBase
        {
            public readonly int DerivedValue;

            public ReadonlyDerived()
            {
            }

            public ReadonlyDerived(int baseValue, int derivedValue)
                : base(baseValue)
            {
                DerivedValue = derivedValue;
            }
        }

        private static T RoundTrip<T>(IBinary binary, T value)
        {
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            stream.Seek(0, SeekOrigin.Begin);
            return binary.Read<T>(stream);
        }

        [Fact]
        public void ReadonlyFieldsRoundTripAndTheirInitOnlyBitIsRestored()
        {
            if (FieldInfoModifier.SetFieldInfoNotReadonly == null)
            {
                return; // The runtime forces the reflection path; there is nothing to restore.
            }

            var field = typeof(WithReadonly).GetField(nameof(WithReadonly.Value))!;

            using var binary = Binary.Create(new Settings().MarkSerializable(x => true));
            RoundTrip(binary, new WithReadonly(5)).Value.Should().Be(5);

            field.Attributes.HasFlag(FieldAttributes.InitOnly).Should().BeTrue(
                "generation has completed, so the flipped InitOnly bit must have been restored");

            // The compiled delegate must keep deserializing the field AFTER the restore — its IL was
            // emitted while the field was writable, and re-executing it is the everyday case.
            RoundTrip(binary, new WithReadonly(7)).Value.Should().Be(7);
            field.Attributes.HasFlag(FieldAttributes.InitOnly).Should().BeTrue();
        }

        [Fact]
        public void ReadonlyFieldsSurviveRegenerationInASecondSettingsPartition()
        {
            if (FieldInfoModifier.SetFieldInfoNotReadonly == null)
            {
                return;
            }

            // Two distinct settings partitions generate two independent read delegates for the SAME
            // runtime FieldInfo — the second generation runs after the first's drain restored the
            // InitOnly bit, which is the path a correct restore must keep working.
            using var first = Binary.Create(new Settings().MarkSerializable(x => true));
            RoundTrip(first, new WithReadonly(5)).Value.Should().Be(5);

            using var second = Binary.Create(new Settings { FlattenClassHierarchy = false }.MarkSerializable(x => true));
            RoundTrip(second, new WithReadonly(6)).Value.Should().Be(6);
            RoundTrip(first, new WithReadonly(8)).Value.Should().Be(8);

            typeof(WithReadonly).GetField(nameof(WithReadonly.Value))!
                .Attributes.HasFlag(FieldAttributes.InitOnly).Should().BeTrue();
        }

        [Fact]
        public void ReadonlyBaseClassFieldsRoundTripUnderANonFlattenedHierarchy()
        {
            // The isolated per-base delegates register and drain through the same bookkeeping.
            using var binary = Binary.Create(new Settings { FlattenClassHierarchy = false }.MarkSerializable(x => true));
            var result = RoundTrip(binary, new ReadonlyDerived(3, 4));
            result.BaseValue.Should().Be(3);
            result.DerivedValue.Should().Be(4);
        }
    }
}
