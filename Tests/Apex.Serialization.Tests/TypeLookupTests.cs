using Apex.Serialization.Internal;
using FluentAssertions;
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class TypeLookupTests
    {
        /// <summary>
        /// A type id as the stream holds it: the byte lengths of the type name and of the assembly name, then
        /// <paramref name="bytes"/>.
        /// </summary>
        private static byte[] Id(int nameLength, int assemblyLength, string bytes)
        {
            var id = new byte[2 * sizeof(int) + Encoding.UTF8.GetByteCount(bytes)];
            BinaryPrimitives.WriteInt32LittleEndian(id, nameLength);
            BinaryPrimitives.WriteInt32LittleEndian(id.AsSpan(sizeof(int)), assemblyLength);
            Encoding.UTF8.GetBytes(bytes, id.AsSpan(2 * sizeof(int)));
            return id;
        }

        [Fact]
        public void IdsDifferingOnlyInTheirLastByteFindTheirOwnTypes()
        {
            var lookup = new TypeLookup();
            lookup.Add(Id(4, 4, "Name, AsmA"), typeof(int));
            lookup.Add(Id(4, 4, "Name, AsmB"), typeof(long));

            lookup.Find(Id(4, 4, "Name, AsmA")).Should().Be(typeof(int));
            lookup.Find(Id(4, 4, "Name, AsmB")).Should().Be(typeof(long));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AnIdThatIsAPrefixOfAnotherFindsItsOwnType(bool prefixFirst)
        {
            var shorter = Id(4, 3, "Name, Asm");
            var longer = Id(4, 3, "Name, Asm2");
            var lookup = new TypeLookup();

            if (prefixFirst)
            {
                lookup.Add(shorter, typeof(int));
                lookup.Add(longer, typeof(long));
            }
            else
            {
                lookup.Add(longer, typeof(long));
                lookup.Add(shorter, typeof(int));
            }

            lookup.Find(shorter).Should().Be(typeof(int));
            lookup.Find(longer).Should().Be(typeof(long));
        }

        [Fact]
        public void TheSameBytesSplitDifferentlyBetweenTypeAndAssemblyNameAreDifferentIds()
        {
            var lookup = new TypeLookup();
            lookup.Add(Id(4, 4, "Name, AsmA"), typeof(int));

            lookup.Find(Id(5, 3, "Name, AsmA")).Should().BeNull();

            lookup.Add(Id(5, 3, "Name, AsmA"), typeof(long));
            lookup.Find(Id(4, 4, "Name, AsmA")).Should().Be(typeof(int));
            lookup.Find(Id(5, 3, "Name, AsmA")).Should().Be(typeof(long));
        }

        [Fact]
        public void AnIdOfAnyLengthFindsItsOwnTypeAndChangingAnyOneOfItsBytesMakesAMiss()
        {
            var ids = IdsOfEveryLength();
            var lookup = new TypeLookup();
            foreach (var id in ids)
            {
                lookup.Add(id, TypeFor(id));
            }

            foreach (var id in ids)
            {
                lookup.Find(id).Should().Be(TypeFor(id));
                lookup.Find(id).Should().Be(TypeFor(id), "finding the id just found takes a shortcut");
                for (var at = 0; at < id.Length; ++at)
                {
                    var changed = (byte[])id.Clone();
                    changed[at] ^= 0x80;
                    lookup.Find(changed).Should().BeNull($"byte {at} of the {id.Length}-byte id differs");
                }
            }

            foreach (var id in ids.Reverse())
            {
                lookup.Find(id).Should().Be(TypeFor(id));
            }
        }

        /// <summary>
        /// Lengths 0-99 cover every case the hash treats differently: under 8 bytes, under 32, and whole and partial
        /// 32-byte blocks.
        /// </summary>
        [Fact]
        public void ChangingAnyOneBitOfAnIdChangesItsHash()
        {
            foreach (var id in IdsOfEveryLength())
            {
                for (var at = 0; at < id.Length; ++at)
                {
                    foreach (var bit in new byte[] { 0x01, 0x80 })
                    {
                        Hash(Flipped(id, (at, bit))).Should().NotBe(Hash(id), $"bit {bit:X2} of byte {at} of the {id.Length}-byte id differs");
                    }
                }
            }
        }

        /// <summary>
        /// A round of <c>rotl((lane ^ word) * K, 29)</c> moves a change to a word's top bit onto bit 28, where the same
        /// change to the next word of that lane cancels it whatever the seed.  This pins that one differential: an
        /// unkeyed round always has some that hold for every seed.
        /// </summary>
        [Fact]
        public void TheRotateBy29DifferentialOfAnXorMultiplyRoundDoesntCancelInTheHash()
        {
            foreach (var id in IdsOfEveryLength())
            {
                for (var at = 0; at + 32 + sizeof(ulong) <= id.Length; ++at)
                {
                    Hash(Flipped(id, (at + 7, 0x80), (at + 32 + 3, 0x10))).Should().NotBe(Hash(id), $"words at {at} and {at + 32} of the {id.Length}-byte id differ");
                }
            }
        }

        private static byte[][] IdsOfEveryLength() =>
            Enumerable.Range(0, 100).Select(length => Enumerable.Range(1, length).Select(i => (byte)(i * 7)).ToArray()).ToArray();

        private static int Hash(byte[] id) => TypeLookup.ContentComparer.Instance.GetHashCode((ReadOnlySpan<byte>)id);

        private static byte[] Flipped(byte[] id, params (int At, byte Bits)[] changes)
        {
            var flipped = (byte[])id.Clone();
            foreach (var (at, bits) in changes)
            {
                flipped[at] ^= bits;
            }

            return flipped;
        }

        private static Type TypeFor(byte[] id)
        {
            var types = new[] { typeof(int), typeof(long), typeof(string), typeof(byte), typeof(short) };
            return types[id.Length % types.Length];
        }

        [Fact]
        public void AnIdNeverAddedIsAMiss()
        {
            var lookup = new TypeLookup();
            lookup.Find(Id(4, 4, "Name, AsmA")).Should().BeNull();

            lookup.Add(Id(4, 4, "Name, AsmA"), typeof(int));

            lookup.Find(Id(4, 4, "Name, AsmC")).Should().BeNull();
            lookup.Find(Id(4, 4, "Name, Asm")).Should().BeNull();
            lookup.Find(ReadOnlySpan<byte>.Empty).Should().BeNull();
        }
    }
}
