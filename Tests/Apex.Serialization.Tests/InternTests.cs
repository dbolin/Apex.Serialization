using System;
using System.IO;
using System.Linq;
using Apex.Serialization.Internal;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class InternTests : AbstractSerializerTestBase
    {
        public static readonly object TheObject = new object();

        [Fact]
        public void InternedObjectShouldKeepReferenceEquality()
        {
            _setupSerializerGraph = s => (s as IBinary).Intern(TheObject);
            RoundTrip(TheObject, (original, loaded) => ReferenceEquals(loaded, TheObject).Should().BeTrue(), s => s.SerializationMode == Mode.Graph);
        }

        public sealed class Marker
        {
        }

        public sealed class Node
        {
            public object? First;
            public object? Second;
            public Node? Next;
        }

        public sealed class Hook
        {
        }

        public sealed class Brittle
        {
            public bool Broken;

            public override int GetHashCode() => Broken ? throw new InvalidOperationException() : 0;
        }

        public sealed class WithASpreadHashCode
        {
            private readonly int _hash;

            public WithASpreadHashCode(int index) => _hash = unchecked((index + 1) * (int)2654435761);

            public override int GetHashCode() => _hash;
        }

        [Fact]
        public void ASerializerWritesAndReadsInternedObjectsTheSameWhateverItHandledBefore()
        {
            var interned = new[] { new Marker(), new Marker(), new Marker() };
            var shared = new Marker();
            var value = new Node
            {
                First = interned[2],
                Second = shared,
                Next = new Node { First = interned[0], Second = interned[2], Next = new Node { First = shared } },
            };
            var farMoreThanTheTablesStartWith = Enumerable.Range(0, 5000).Select(_ => new Marker()).ToArray();
            var expected = WrittenByAFreshSerializer(GraphSettings(), value, interned);

            using var binary = Binary.Create(GraphSettings());
            binary.Intern(interned[0]);
            Write(binary, farMoreThanTheTablesStartWith);
            binary.Intern(interned[1]);
            binary.Intern(interned[2]);

            object? sharedInThePassBefore = null;
            for (var pass = 0; pass < 2; ++pass)
            {
                Write(binary, value).Should().Equal(expected);
                Write(binary, value).Should().Equal(expected);
                var read = binary.Read<Node>(new MemoryStream(expected));
                read.First.Should().BeSameAs(interned[2]);
                read.Second.Should().BeOfType<Marker>().And.NotBeSameAs(sharedInThePassBefore);
                read.Next!.First.Should().BeSameAs(interned[0]);
                read.Next.Second.Should().BeSameAs(interned[2]);
                read.Next.Next!.First.Should().BeSameAs(read.Second);
                sharedInThePassBefore = read.Second;

                binary.Read<Marker[]>(new MemoryStream(Write(binary, farMoreThanTheTablesStartWith)))
                    .Should().HaveCount(farMoreThanTheTablesStartWith.Length);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnObjectInternedInTheMiddleOfAStreamIsInternedForTheStreamsAfterIt(bool duringRead)
        {
            var late = new Marker();
            var value = SharingAMarker(late);
            var holdsAHook = new Node { First = new Marker(), Second = new Hook() };
            Action hook = () => { };
            var settings = SettingsRunning(onWrite: () => hook(), onRead: () => hook());
            var expected = WrittenByAFreshSerializer(settings, value, late);

            using var binary = Binary.Create(settings);
            var payload = Write(binary, holdsAHook);
            binary.Read<Node>(new MemoryStream(payload));
            var interned = 0;
            hook = () =>
            {
                binary.Intern(late);
                ++interned;
            };
            if (duringRead)
            {
                binary.Read<Node>(new MemoryStream(payload));
            }
            else
            {
                Write(binary, holdsAHook);
            }

            hook = () => { };
            interned.Should().Be(1);

            for (var pass = 0; pass < 2; ++pass)
            {
                Write(binary, value).Should().Equal(expected);
                ShouldReadBackSharingAMarker(binary, expected, late);
            }
        }

        [Fact]
        public void AStreamThatFailsPartwayLeavesTheInternedObjectsInPlace()
        {
            var interned = new Marker();
            var value = SharingAMarker(interned);
            var failing = new Node { First = interned, Second = new Marker(), Next = new Node { First = new Hook() } };
            Action hook = () => { };
            var settings = SettingsRunning(onWrite: () => hook(), onRead: () => { });
            var expected = WrittenByAFreshSerializer(settings, value, interned);
            using var binary = Binary.Create(settings);
            binary.Intern(interned);

            for (var pass = 0; pass < 2; ++pass)
            {
                hook = () => throw new InvalidOperationException();
                binary.Invoking(b => Write(b, failing)).Should().Throw<InvalidOperationException>();
                hook = () => { };
                Write(binary, value).Should().Equal(expected);

                binary.Invoking(b => b.Read<Node>(new MemoryStream(expected[..^1]))).Should().Throw<EndOfStreamException>();
                ShouldReadBackSharingAMarker(binary, expected, interned);
            }
        }

        [Fact]
        public void ASerializerThatInternedAnObjectTwiceWritesTheSameBytesEachTime()
        {
            var interned = new Marker();
            var value = SharingAMarker(interned);
            using var binary = Binary.Create(GraphSettings());
            binary.Intern(interned);
            binary.Intern(interned);

            var first = Write(binary, value);

            Write(binary, value).Should().Equal(first);
            Write(binary, value).Should().Equal(first);
        }

        [Fact]
        public void ASerializerThatInternedAnObjectTwiceReadsTheSameEachTime()
        {
            var interned = new Marker();
            using var binary = Binary.Create(GraphSettings());
            binary.Intern(interned);
            binary.Intern(interned);
            var payload = Write(binary, SharingAMarker(interned));

            var first = WhatEachFieldRefersTo(binary.Read<Node>(new MemoryStream(payload)), interned);

            for (var pass = 0; pass < 3; ++pass)
            {
                WhatEachFieldRefersTo(binary.Read<Node>(new MemoryStream(payload)), interned).Should().Be(first);
            }
        }

        [Fact]
        public void InternedObjectsStayInternedThroughStreamsThatOutgrowTheTables()
        {
            var interned = Enumerable.Range(0, 5).Select(i => new WithASpreadHashCode(i)).ToArray();
            var shared = new Marker();
            var value = new Node
            {
                First = interned[4],
                Second = shared,
                Next = new Node
                {
                    First = interned[0],
                    Second = interned[2],
                    Next = new Node { First = shared, Second = interned[1], Next = new Node { First = interned[3] } },
                },
            };
            var expected = WrittenByAFreshSerializer(GraphSettings(), value, interned);
            using var binary = Binary.Create(GraphSettings());
            foreach (var o in interned)
            {
                binary.Intern(o);
            }

            for (var count = 1; count <= 4096; count *= 4)
            {
                var largerThanAnyStreamBefore = Enumerable.Range(0, count).Select(_ => new Marker()).ToArray();
                binary.Read<Marker[]>(new MemoryStream(Write(binary, largerThanAnyStreamBefore))).Should().HaveCount(count);

                Write(binary, value).Should().Equal(expected, "after a stream of {0} objects", count);
                var read = binary.Read<Node>(new MemoryStream(expected));
                read.First.Should().BeSameAs(interned[4]);
                read.Second.Should().BeOfType<Marker>();
                read.Next!.First.Should().BeSameAs(interned[0]);
                read.Next.Second.Should().BeSameAs(interned[2]);
                read.Next.Next!.First.Should().BeSameAs(read.Second);
                read.Next.Next.Second.Should().BeSameAs(interned[1]);
                read.Next.Next.Next!.First.Should().BeSameAs(interned[3]);
            }
        }

        [Fact]
        public void AnObjectWhoseInternThrewIsInternedOnceAWriteAndAReadHaveEnded()
        {
            var interned = new Marker();
            var brittle = new Brittle();
            var value = new Node { First = interned, Second = brittle, Next = SharingAMarker(brittle) };
            var expected = WrittenByAFreshSerializer(GraphSettings(), value, interned, brittle);
            brittle.Broken = true;
            using var binary = SerializerWhoseInternThrew(interned, brittle);
            brittle.Broken = false;
            binary.Read<Node>(new MemoryStream(Write(binary, value)));

            for (var pass = 0; pass < 2; ++pass)
            {
                Write(binary, value).Should().Equal(expected);
                var read = binary.Read<Node>(new MemoryStream(expected));
                read.First.Should().BeSameAs(interned);
                read.Second.Should().BeSameAs(brittle);
                read.Next!.First.Should().BeSameAs(brittle);
                read.Next.Second.Should().BeOfType<Marker>();
                read.Next.Next!.First.Should().BeSameAs(read.Next.Second);
            }
        }

        [Fact]
        public void WhileAnObjectWhoseInternThrewStillThrowsEveryWriteIsRefused()
        {
            var interned = new Marker();
            using var binary = SerializerWhoseInternThrew(interned, new Brittle { Broken = true });

            for (var pass = 0; pass < 2; ++pass)
            {
                binary.Invoking(b => Write(b, SharingAMarker(interned))).Should().Throw<InvalidOperationException>();
            }
        }

        /// <summary>
        /// The serializer writes and reads once before the <see cref="IBinary.Intern"/> that throws, since only a table
        /// that has kept its interned entries from an earlier stream can be left out of step with them.
        /// </summary>
        private static IBinary SerializerWhoseInternThrew(Marker interned, Brittle brittle)
        {
            var binary = Binary.Create(GraphSettings());
            binary.Intern(interned);
            binary.Read<Node>(new MemoryStream(Write(binary, SharingAMarker(interned))));
            binary.Invoking(b => b.Intern(brittle)).Should().Throw<InvalidOperationException>();
            return binary;
        }

        private static Settings GraphSettings()
        {
            return new Settings { SerializationMode = Mode.Graph }.MarkSerializable(_ => true);
        }

        private static Settings SettingsRunning(Action onWrite, Action onRead)
        {
            return new Settings { SerializationMode = Mode.Graph, SupportSerializationHooks = true }.MarkSerializable(_ => true)
                .RegisterCustomSerializer<Hook>((_, _) => onWrite(), (_, _) => onRead());
        }

        private static byte[] WrittenByAFreshSerializer(Settings settings, Node value, params object[] interned)
        {
            using var fresh = Binary.Create(settings);
            foreach (var o in interned)
            {
                fresh.Intern(o);
            }

            return Write(fresh, value);
        }

        private static Node SharingAMarker(object first)
        {
            var shared = new Marker();
            return new Node { First = first, Second = shared, Next = new Node { First = shared } };
        }

        private static string WhatEachFieldRefersTo(Node read, object interned)
        {
            string Name(object? o) => ReferenceEquals(o, interned) ? "interned"
                : ReferenceEquals(o, read) ? "root"
                : ReferenceEquals(o, read.Second) ? "second"
                : ReferenceEquals(o, read.Next) ? "next"
                : o == null ? "null" : o.GetType().Name;
            return $"{Name(read.First)},{read.Second?.GetType().Name},{Name(read.Next?.First)}";
        }

        private static void ShouldReadBackSharingAMarker(IBinary binary, byte[] payload, object first)
        {
            var read = binary.Read<Node>(new MemoryStream(payload));
            read.First.Should().BeSameAs(first);
            read.Second.Should().BeOfType<Marker>();
            read.Next!.First.Should().BeSameAs(read.Second);
        }

        private static byte[] Write<T>(IBinary binary, T value)
        {
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }
    }
}
