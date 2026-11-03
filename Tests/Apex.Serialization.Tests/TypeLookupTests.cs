using Apex.Serialization.Internal;
using FluentAssertions;
using System;
using System.Buffers.Binary;
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
