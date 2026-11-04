using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// A serializer remembers how it read the last root type, so it has to tell the next root type apart from it, and
    /// still read a null root without first generating code for the root's type.
    /// </summary>
    public class RootTypeTests
    {
        public sealed class First
        {
            public int Value;
        }

        public sealed class Second
        {
            public string? Name;
        }

        public sealed class Unreadable
        {
            public int Value;
        }

        private static Settings WithoutVersionIds() => new Settings { UseSerializedVersionId = false };

        [Fact]
        public void OneSerializerReadsSealedAndPolymorphicRootsOfDifferentTypesInTurn()
        {
            using var binary = Binary.Create(WithoutVersionIds().MarkSerializable(_ => true));
            var first = Written(binary, new First { Value = 7 });
            var second = Written(binary, new Second { Name = "second" });
            var polymorphic = Written<object>(binary, new First { Value = 9 });

            for (var i = 0; i < 2; ++i)
            {
                binary.Read<First>(new MemoryStream(first)).Value.Should().Be(7);
                binary.Read<Second>(new MemoryStream(second)).Name.Should().Be("second");
                binary.Read<object>(new MemoryStream(polymorphic)).Should().BeOfType<First>().Which.Value.Should().Be(9);
                binary.Read<First>(new MemoryStream(first)).Value.Should().Be(7);
            }
        }

        [Fact]
        public void ANullRootOfATypeTheSettingsDontMarkSerializableReadsAsNull()
        {
            using var writer = Binary.Create(WithoutVersionIds().MarkSerializable(_ => true));
            using var reader = Binary.Create(WithoutVersionIds().MarkSerializable(x => x == typeof(First)));

            reader.Read<Unreadable?>(new MemoryStream(Written<Unreadable?>(writer, null))).Should().BeNull();
            reader.Read<Unreadable?>(new MemoryStream(Written<Unreadable?>(writer, null))).Should().BeNull();
            var readOne = () => reader.Read<Unreadable>(new MemoryStream(Written(writer, new Unreadable())));
            readOne.Should().Throw<InvalidOperationException>().WithMessage("*not marked as serializable*");
        }

        private static byte[] Written<T>(IBinary binary, T value)
        {
            using var written = new MemoryStream();
            binary.Write(value, written);
            return written.ToArray();
        }
    }
}
