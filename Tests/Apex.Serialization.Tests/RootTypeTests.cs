using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public class Animal
        {
            public int Legs;
        }

        public sealed class Dog : Animal
        {
            public string? Name;
        }

        public sealed class Tag
        {
            public string? Label;
        }

        /// <summary>
        /// Written and read by custom code that alternates a polymorphic and a sealed nested object.
        /// </summary>
        public sealed class Alternating
        {
            public readonly List<Animal> Animals = new List<Animal>();
            public readonly List<Tag> Tags = new List<Tag>();
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

        [Fact]
        public void ACustomReaderAlternatingTwoTypesReadsEachObject()
        {
            var settings = WithoutVersionIds().MarkSerializable(_ => true)
                .RegisterCustomSerializer<Alternating>((value, writer) =>
                {
                    writer.Write(value.Animals.Count);
                    for (var i = 0; i < value.Animals.Count; ++i)
                    {
                        writer.WriteObject(value.Animals[i]);
                        writer.WriteObject(value.Tags[i]);
                    }
                }, (_, _) => { })
                .RegisterCustomInstantiator<Alternating>(reader =>
                {
                    var read = new Alternating();
                    var count = reader.Read<int>();
                    for (var i = 0; i < count; ++i)
                    {
                        read.Animals.Add(reader.ReadObject<Animal>());
                        read.Tags.Add(reader.ReadObject<Tag>());
                    }

                    return read;
                });
            settings.SupportSerializationHooks = true;
            var written = new Alternating();
            for (var i = 0; i < 3; ++i)
            {
                written.Animals.Add(i % 2 == 0 ? new Dog { Legs = 4, Name = $"dog {i}" } : new Animal { Legs = i });
                written.Tags.Add(new Tag { Label = $"tag {i}" });
            }

            using var binary = Binary.Create(settings);
            var read = binary.Read<Alternating>(new MemoryStream(Written(binary, written)));

            static (Type, int, string?) Shown(Animal animal) => (animal.GetType(), animal.Legs, (animal as Dog)?.Name);
            read.Animals.Select(Shown).Should().Equal(written.Animals.Select(Shown));
            read.Tags.Select(x => x.Label).Should().Equal("tag 0", "tag 1", "tag 2");
        }

        private static byte[] Written<T>(IBinary binary, T value)
        {
            using var written = new MemoryStream();
            binary.Write(value, written);
            return written.ToArray();
        }
    }
}
