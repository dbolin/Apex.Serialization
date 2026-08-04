using Apex.Serialization.Attributes;
using Apex.Serialization.Extensions;
using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class BoundaryTests
    {
        public class Boundary
        {
            public int Id;
            public string? Payload;
        }

        public sealed class DerivedBoundary : Boundary
        {
            public double Extra;
        }

        public sealed class UnserializableBoundary
        {
            public IntPtr Handle;
            public UnwhitelistedNested? Nested;
        }

        public sealed class UnwhitelistedNested
        {
            public IntPtr AlsoAHandle;
        }

        public sealed class Sibling
        {
            public int Number;
            public string? Text;
        }

        public sealed class Holder
        {
            public Boundary? First;
            public Boundary? Second;
            public Sibling? Sibling;
            public int Value;
        }

        public sealed class PolymorphicHolder
        {
            public object? Anything;
            public int Value;
        }

        public sealed class DerivedHolder
        {
            public DerivedBoundary? Derived;
            public int Value;
        }

        public sealed class UnserializableHolder
        {
            public UnserializableBoundary? Boundary;
            public int Value;
        }

        public sealed class TwoBoundaryHolder
        {
            public Boundary? First;
            public UnserializableBoundary? Second;
        }

        public sealed class SharedSiblingHolder
        {
            public Sibling? A;
            public Sibling? B;
            public Boundary? Boundary;
        }

        public sealed class Hooked
        {
            public static int Calls;

            public int Value;

            [AfterDeserialization]
            public void AfterDeserialization()
            {
                Calls++;
            }
        }

        public sealed class HookHolder
        {
            // Reference fields are read in name order, so the hooked field must sort before the boundary
            // field for its hook to be queued before the missing-substitute abort.
            public Hooked? Hooked;
            public Boundary? Unresolved;
        }

        public sealed class DictionaryHolder
        {
            public System.Collections.Generic.Dictionary<Boundary, int> ByBoundary = new();
        }

        public sealed class SealedBoundary
        {
            public int Id;
            public string? Payload;
        }

        public sealed class SealedBoundaryHolder
        {
            public SealedBoundary? Boundary;
            public int Value;
            public string? Text;
        }

        private static Settings BoundarySettings()
        {
            return new Settings { SerializationMode = Mode.Graph, UseSerializedVersionId = true }
                .MarkSerializable(typeof(Sibling))
                .MarkSerializable(typeof(Holder))
                .MarkSerializable(typeof(PolymorphicHolder))
                .MarkSerializable(typeof(DerivedHolder))
                .MarkSerializable(typeof(UnserializableHolder))
                .MarkSerializable(typeof(TwoBoundaryHolder))
                .MarkSerializable(typeof(SharedSiblingHolder))
                .MarkBoundary<Boundary>()
                .MarkBoundary<UnserializableBoundary>();
        }

        private static byte[] Write<T>(Settings settings, T value, Action<IBinary>? setup = null)
        {
            using var sut = Binary.Create(settings);
            setup?.Invoke(sut);
            using var stream = new MemoryStream();
            sut.Write(value, stream);
            return stream.ToArray();
        }

        private static T ReadWithSubstitute<T>(Settings settings, byte[] payload, Action<IBinary>? setup)
        {
            using var sut = Binary.Create(settings);
            setup?.Invoke(sut);
            using var stream = new MemoryStream(payload);
            return sut.Read<T>(stream);
        }

        private static T RoundTrip<T>(Settings settings, T value, Action<IBinary>? readSetup)
        {
            return ReadWithSubstitute<T>(settings, Write(settings, value), readSetup);
        }

        [Fact]
        public void BoundaryFieldResolvesToSubstituteWhileSiblingsDeserialize()
        {
            var settings = BoundarySettings();
            var substitute = new Boundary { Id = 99, Payload = "substitute" };
            var original = new Holder
            {
                First = new Boundary { Id = 1, Payload = "written" },
                Sibling = new Sibling { Number = 5, Text = "sibling" },
                Value = 42,
            };

            var loaded = RoundTrip(settings, original, b => b.SetBoundarySubstitute(substitute));

            loaded.First.Should().BeSameAs(substitute);
            loaded.Value.Should().Be(42);
            loaded.Sibling!.Number.Should().Be(5);
            loaded.Sibling.Text.Should().Be("sibling");
        }

        [Fact]
        public void SameBoundaryInstanceInTwoFieldsAliasesTheSubstitute()
        {
            var settings = BoundarySettings();
            var substitute = new Boundary { Id = 99 };
            var shared = new Boundary { Id = 1 };
            var original = new Holder { First = shared, Second = shared };

            var loaded = RoundTrip(settings, original, b => b.SetBoundarySubstitute(substitute));

            loaded.First.Should().BeSameAs(substitute);
            loaded.Second.Should().BeSameAs(substitute);
        }

        [Fact]
        public void DistinctBoundaryInstancesBothResolveToTheOneSubstitute()
        {
            var settings = BoundarySettings();
            var substitute = new Boundary { Id = 99 };
            var original = new Holder
            {
                First = new Boundary { Id = 1 },
                Second = new Boundary { Id = 2 },
            };

            var loaded = RoundTrip(settings, original, b => b.SetBoundarySubstitute(substitute));

            loaded.First.Should().BeSameAs(substitute);
            loaded.Second.Should().BeSameAs(substitute);
        }

        [Fact]
        public void PolymorphicFieldHoldingABoundaryInstanceResolves()
        {
            var settings = BoundarySettings();
            var substitute = new Boundary { Id = 99 };
            var original = new PolymorphicHolder { Anything = new Boundary { Id = 1 }, Value = 7 };

            var loaded = RoundTrip(settings, original, b => b.SetBoundarySubstitute(substitute));

            loaded.Anything.Should().BeSameAs(substitute);
            loaded.Value.Should().Be(7);
        }

        [Fact]
        public void SubclassOfMarkedTypeResolvesViaTheMarkedBaseKey()
        {
            var settings = BoundarySettings();
            var substitute = new DerivedBoundary { Id = 99, Extra = 1.5 };
            var original = new DerivedHolder { Derived = new DerivedBoundary { Id = 1, Extra = 2.5 }, Value = 3 };

            // Registered under the marked base type, not the concrete subclass in the payload.
            var loaded = RoundTrip(settings, original, b => b.SetBoundarySubstitute(typeof(Boundary), substitute));

            loaded.Derived.Should().BeSameAs(substitute);
            loaded.Value.Should().Be(3);
        }

        [Fact]
        public void WriteSucceedsWithoutASubstituteButReadThrows()
        {
            var settings = BoundarySettings();
            var original = new Holder { First = new Boundary { Id = 1 } };

            var payload = Write(settings, original);

            payload.Should().NotBeEmpty();
            var read = () => ReadWithSubstitute<Holder>(settings, payload, null);
            read.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{typeof(Boundary).FullName}*SetBoundarySubstitute*");
        }

        [Fact]
        public void BoundaryTypeIsNotTraversedThroughItsBaseClassWhenHierarchyIsNotFlattened()
        {
            // With FlattenClassHierarchy off, a non-boundary type walks its base chain and emits a write
            // call per base type. A boundary subclass must skip that walk as well, or the payload
            // includes base-class fields the read side never consumes.
            var settings = new Settings { SerializationMode = Mode.Graph, FlattenClassHierarchy = false }
                .MarkSerializable(typeof(DerivedHolder))
                .MarkBoundary<Boundary>();
            var substitute = new DerivedBoundary { Id = 99, Extra = 1.5 };
            var original = new DerivedHolder { Derived = new DerivedBoundary { Id = 1, Extra = 2.5 }, Value = 8 };

            var loaded = RoundTrip(settings, original, b => b.SetBoundarySubstitute(typeof(Boundary), substitute));

            loaded.Derived.Should().BeSameAs(substitute);
            loaded.Value.Should().Be(8);
        }

        internal static void WriteBoundary(Boundary b, IBinaryWriter writer) => writer.Write(b.Id);

        internal static void ReadBoundary(Boundary b, IBinaryReader reader) => b.Id = reader.Read<int>();

        [Fact]
        public void BoundaryMarkingWinsOverARegisteredCustomSerializer()
        {
            // A custom serializer for the same type would otherwise emit a body on write and consume it
            // on read; the boundary early-out must take precedence on both sides so the two stay in sync.
            var settings = new Settings { SerializationMode = Mode.Graph, SupportSerializationHooks = true }
                .MarkSerializable(typeof(Holder))
                .MarkSerializable(typeof(Sibling))
                .RegisterCustomSerializer<Boundary>(WriteBoundary, ReadBoundary)
                .MarkBoundary<Boundary>();
            var substitute = new Boundary { Id = 99 };
            var original = new Holder { First = new Boundary { Id = 1 }, Value = 6 };

            var loaded = RoundTrip(settings, original, b => b.SetBoundarySubstitute(substitute));

            loaded.First.Should().BeSameAs(substitute);
            loaded.First!.Id.Should().Be(99);
            loaded.Value.Should().Be(6);
        }

        [Fact]
        public void BoundaryTypeWithUnserializableMembersNeedsNoWhitelistingAndRoundTrips()
        {
            var settings = BoundarySettings();
            var substitute = new UnserializableBoundary { Handle = new IntPtr(7) };
            var original = new UnserializableHolder
            {
                // Neither UnserializableBoundary nor UnwhitelistedNested is marked serializable, and
                // both contain IntPtr members - only skipping traversal entirely makes this work.
                Boundary = new UnserializableBoundary { Handle = new IntPtr(1), Nested = new UnwhitelistedNested() },
                Value = 11,
            };

            var loaded = RoundTrip(settings, original, b => b.SetBoundarySubstitute(substitute));

            loaded.Boundary.Should().BeSameAs(substitute);
            loaded.Value.Should().Be(11);
        }

        [Fact]
        public void InternedBoundaryInstanceRestoresTheOriginalReferenceWithoutConsultingTheSubstitute()
        {
            var settings = BoundarySettings();
            var interned = new Boundary { Id = 1, Payload = "interned" };
            var original = new Holder { First = interned, Value = 4 };

            using var writer = Binary.Create(settings);
            writer.Intern(interned);
            using var stream = new MemoryStream();
            writer.Write(original, stream);

            using var reader = Binary.Create(settings);
            reader.Intern(interned);
            stream.Position = 0;
            // No substitute registered: the ref path must win before the boundary body would run.
            var loaded = reader.Read<Holder>(stream);

            loaded.First.Should().BeSameAs(interned);
            loaded.Value.Should().Be(4);
        }

        [Fact]
        public void SubstitutesAreClearedAfterEachRead()
        {
            var settings = BoundarySettings();
            var substitute = new Boundary { Id = 99 };
            var payload = Write(settings, new Holder { First = new Boundary { Id = 1 } });

            using var sut = Binary.Create(settings);
            sut.SetBoundarySubstitute(substitute);
            using var stream = new MemoryStream(payload);
            sut.Read<Holder>(stream).First.Should().BeSameAs(substitute);

            stream.Position = 0;
            var secondRead = () => sut.Read<Holder>(stream);
            secondRead.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{typeof(Boundary).FullName}*SetBoundarySubstitute*");
        }

        [Fact]
        public void ReRegisteringADifferentSubstituteBetweenReadsTakesEffect()
        {
            var settings = BoundarySettings();
            var payload = Write(settings, new Holder { First = new Boundary { Id = 1 } });
            var first = new Boundary { Id = 98 };
            var second = new Boundary { Id = 99 };

            using var sut = Binary.Create(settings);
            sut.SetBoundarySubstitute(first);
            sut.Read<Holder>(new MemoryStream(payload)).First.Should().BeSameAs(first);

            sut.SetBoundarySubstitute(second);
            sut.Read<Holder>(new MemoryStream(payload)).First.Should().BeSameAs(second);
        }

        [Fact]
        public void BoundaryTypeAsTheWriteRootResolvesToTheSubstitute()
        {
            var settings = BoundarySettings();
            var substitute = new Boundary { Id = 99 };

            var loaded = RoundTrip<Boundary>(settings, new Boundary { Id = 1 }, b => b.SetBoundarySubstitute(substitute));

            loaded.Should().BeSameAs(substitute);
        }

        [Theory]
        // Asserts WHICH rejection fired, so a redundant or dead entry in one guard cannot hide behind
        // another guard that happens to reject the same type.
        [InlineData(typeof(int), "not a reference type")]
        [InlineData(typeof(DateTime), "not a reference type")]
        [InlineData(typeof(string), "without traversing fields")]
        [InlineData(typeof(int[]), "without traversing fields")]
        [InlineData(typeof(Boundary[]), "without traversing fields")]
        [InlineData(typeof(Action), "without traversing fields")]
        [InlineData(typeof(Func<int>), "without traversing fields")]
        [InlineData(typeof(Type), "without traversing fields")]
        // Delegate and MulticastDelegate are caught by the delegate check, not by the base-type set.
        [InlineData(typeof(Delegate), "without traversing fields")]
        [InlineData(typeof(MulticastDelegate), "without traversing fields")]
        [InlineData(typeof(IDisposable), "matched by class hierarchy")]
        // Boundary matching walks BaseType, so marking one of these would make unrelated types
        // boundaries and bypass the per-type rejections above.
        [InlineData(typeof(object), "unrelated types derive from it")]
        [InlineData(typeof(ValueType), "unrelated types derive from it")]
        [InlineData(typeof(Enum), "unrelated types derive from it")]
        [InlineData(typeof(Array), "unrelated types derive from it")]
        // Open generic definitions never match a constructed type, so marking one silently does nothing.
        [InlineData(typeof(System.Collections.Generic.List<>), "open generic type definition")]
        public void UnsupportedBoundaryTypesAreRejected(Type type, string expectedReason)
        {
            var mark = () => new Settings().MarkBoundary(type);
            mark.Should().Throw<ArgumentException>().WithMessage($"*{expectedReason}*");
        }

        [Fact]
        public void GenericMarkBoundaryRejectsUnsupportedReferenceTypes()
        {
            var markString = () => new Settings().MarkBoundary<string>();
            markString.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void BoundarySettingsInTreeModeAreRejectedAtSerializerConstruction()
        {
            var settings = new Settings { SerializationMode = Mode.Tree }
                .MarkSerializable(typeof(Holder))
                .MarkSerializable(typeof(Sibling))
                .MarkBoundary<Boundary>();

            // Rejected at construction rather than at code generation, so the direction that gets
            // generated first cannot decide whether the error surfaces.
            var create = () => Binary.Create(settings);

            create.Should().Throw<InvalidOperationException>().WithMessage("*Graph*");
        }

        [Fact]
        public void BoundarySettingsInTreeModeAreRejectedBeforeAnyRead()
        {
            var settings = new Settings { SerializationMode = Mode.Tree }
                .MarkSerializable(typeof(Holder))
                .MarkBoundary<Boundary>();

            var toImmutable = () => settings.ToImmutable();

            toImmutable.Should().Throw<InvalidOperationException>().WithMessage("*Graph*");
        }

        [Fact]
        public void NullSubstituteIsRejected()
        {
            using var sut = Binary.Create(BoundarySettings());

            var setNull = () => sut.SetBoundarySubstitute(typeof(Boundary), null!);

            setNull.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void SubstituteNotAssignableToTheMarkedTypeIsRejected()
        {
            using var sut = Binary.Create(BoundarySettings());

            // Rejected up front because assignability to the MARKED type is knowable here.
            var setWrongType = () => sut.SetBoundarySubstitute(typeof(Boundary), "not a boundary");

            setWrongType.Should().Throw<ArgumentException>()
                .WithMessage($"*{typeof(Boundary).FullName}*");
        }

        [Fact]
        public void SubstituteNotAssignableToTheConcretePayloadTypeFailsDiagnosticallyOnRead()
        {
            var settings = BoundarySettings();
            // A base-typed substitute satisfies the setter's check against the marked type, but the
            // payload holds a subclass. The concrete types are unknowable until the read, so this can
            // only be caught there - and must name the types rather than surfacing a bare cast failure.
            var baseSubstitute = new Boundary { Id = 99 };
            var payload = Write(settings, new DerivedHolder { Derived = new DerivedBoundary { Id = 1 } });

            var read = () => ReadWithSubstitute<DerivedHolder>(settings, payload,
                b => b.SetBoundarySubstitute(typeof(Boundary), baseSubstitute));

            read.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{typeof(Boundary).FullName}*")
                .WithMessage($"*{typeof(DerivedBoundary).FullName}*")
                .WithMessage("*assignable to every concrete boundary type*");
        }

        [Fact]
        public void SubstituteForAnUnmarkedTypeIsRejected()
        {
            using var sut = Binary.Create(BoundarySettings());

            // Would otherwise be a silent no-op that surfaces as a missing-substitute failure later.
            var setUnmarked = () => sut.SetBoundarySubstitute(new Sibling());

            setUnmarked.Should().Throw<ArgumentException>()
                .WithMessage($"*{typeof(Sibling).FullName}*MarkBoundary*");
        }

        [Fact]
        public void GenericOverloadWithASubclassTypedVariableRegistersUnderTheMarkedBase()
        {
            var settings = BoundarySettings();
            // Static type is the subclass, so typeof(T) is DerivedBoundary while the payload resolves
            // against the marked base - the registration has to be keyed to the base regardless.
            DerivedBoundary substitute = new DerivedBoundary { Id = 99, Extra = 1.5 };
            var original = new DerivedHolder { Derived = new DerivedBoundary { Id = 1 }, Value = 2 };

            var loaded = RoundTrip(settings, original, b => b.SetBoundarySubstitute(substitute));

            loaded.Derived.Should().BeSameAs(substitute);
            loaded.Value.Should().Be(2);
        }

        [Fact]
        public void SetBoundarySubstituteInTreeModeThrows()
        {
            var settings = new Settings { SerializationMode = Mode.Tree }.MarkSerializable(typeof(Sibling));

            using var sut = Binary.Create(settings);
            var set = () => sut.SetBoundarySubstitute(new Boundary());
            set.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void PayloadWrittenWithBoundarySettingsIsRejectedByUnmarkedSettings()
        {
            var boundarySettings = BoundarySettings();
            var plainSettings = new Settings { SerializationMode = Mode.Graph, UseSerializedVersionId = true }
                .MarkSerializable(typeof(Boundary))
                .MarkSerializable(typeof(Sibling))
                .MarkSerializable(typeof(Holder));

            var payload = Write(boundarySettings, new Holder { First = new Boundary { Id = 1 } });

            var read = () => ReadWithSubstitute<Holder>(plainSettings, payload, null);
            read.Should().Throw<InvalidOperationException>()
                .WithMessage("*SerializedVersionUniqueId does not match*");
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(0, false)]
        [InlineData(10, true)]
        [InlineData(10, false)]
        public void CrossSettingsReadFailsOnTheVersionId(int inliningMaxDepth, bool writeWithBoundary)
        {
            // The boundary field is a SEALED type, so it goes through the sealed dispatch path, which
            // passes useSerializedVersionId: false and writes no per-type id of its own. Unless the
            // boundary configuration reaches the id of the enclosing type, a mixed-settings read here
            // either crashes undiagnostically or - at depth 0, writing plain and reading boundary -
            // silently succeeds with a substitute in place of real data.
            Settings Make(bool boundary)
            {
                var s = new Settings { SerializationMode = Mode.Graph, InliningMaxDepth = inliningMaxDepth }
                    .MarkSerializable(typeof(SealedBoundaryHolder));
                return boundary ? s.MarkBoundary<SealedBoundary>() : s.MarkSerializable(typeof(SealedBoundary));
            }

            var graph = new SealedBoundaryHolder
            {
                Boundary = new SealedBoundary { Id = 1, Payload = "written" },
                Value = 7,
                Text = "text",
            };

            var payload = writeWithBoundary
                ? Write(Make(true), graph, b => b.SetBoundarySubstitute(new SealedBoundary()))
                : Write(Make(false), graph);

            var read = () => ReadWithSubstitute<SealedBoundaryHolder>(Make(!writeWithBoundary), payload,
                writeWithBoundary ? null : b => b.SetBoundarySubstitute(new SealedBoundary { Id = 99 }));

            read.Should().Throw<InvalidOperationException>()
                .WithMessage("*SerializedVersionUniqueId does not match*");
        }

        public sealed class GameState
        {
            public IntPtr NativeHandle;
            public int[]? HugePayload;
        }

        public sealed class Player
        {
            public string? Name;
            public GameState? State;
        }

        [Fact]
        public void ReadmeExampleBehavesAsDocumented()
        {
            var settings = new Settings { SerializationMode = Mode.Graph }
                .MarkSerializable(typeof(Player))
                .MarkBoundary<GameState>();

            var currentGameState = new GameState { NativeHandle = new IntPtr(7) };
            var player = new Player { Name = "player", State = new GameState { HugePayload = new[] { 1, 2, 3 } } };

            using var serializer = Binary.Create(settings);
            using var stream = new MemoryStream();
            serializer.Write(player, stream);

            serializer.SetBoundarySubstitute(currentGameState);
            stream.Position = 0;
            var loadedPlayer = serializer.Read<Player>(stream);

            loadedPlayer.State.Should().BeSameAs(currentGameState);
            loadedPlayer.Name.Should().Be("player");
        }

        [Fact]
        public void BoundaryTypeUsedAsADictionaryKeyCollapsesToOneEntry()
        {
            // Documents an inherent consequence of aliasing rather than asserting desirable behavior:
            // every boundary occurrence resolves to the one substitute, so distinct keys collapse to a
            // single identity and the restored collection no longer matches what was written.
            var settings = new Settings { SerializationMode = Mode.Graph }
                .MarkSerializable(typeof(DictionaryHolder))
                .MarkSerializable(typeof(System.Collections.Generic.Dictionary<,>))
                .MarkBoundary<Boundary>();
            var original = new DictionaryHolder();
            original.ByBoundary[new Boundary { Id = 1 }] = 10;
            original.ByBoundary[new Boundary { Id = 2 }] = 20;

            var substitute = new Boundary { Id = 99 };
            var loaded = RoundTrip(settings, original, b => b.SetBoundarySubstitute(substitute));

            loaded.ByBoundary.Keys.Should().OnlyContain(k => ReferenceEquals(k, substitute));
            loaded.ByBoundary.Keys.Should().HaveCount(2, "the collapse corrupts lookup rather than deduplicating");
            loaded.ByBoundary.TryGetValue(substitute, out _).Should().BeFalse(
                "a Dictionary with two entries hashing to one key identity can no longer find either");
        }

        public static readonly TheoryData<bool, int> SettingsVariants = new()
        {
            // Default, plus the two variants that change how the write-side early-out's surroundings are
            // generated: the base-chain walk it wraps, and the sealed-dispatch path.
            { true, 10 },
            { false, 10 },
            { true, 0 },
        };

        [Theory]
        [MemberData(nameof(SettingsVariants))]
        public void MarkingABoundaryTypeMakesEveryTypesPayloadDistinct(bool flattenClassHierarchy, int inliningMaxDepth)
        {
            Settings Configure(Settings s)
            {
                s.FlattenClassHierarchy = flattenClassHierarchy;
                s.InliningMaxDepth = inliningMaxDepth;
                return s;
            }

            var plainSettings = Configure(BoundaryWriteDriftTests.PinnedSettings(Mode.Graph));
            var withUnrelatedBoundary = Configure(BoundaryWriteDriftTests.PinnedSettings(Mode.Graph))
                .MarkBoundary<Boundary>();

            var graph = BoundaryWriteDriftTests.BuildPinnedGraph();

            // Deliberate: the boundary configuration reaches the version id of every type, so even a
            // graph containing no boundary type serializes differently. That is what makes a payload
            // written under boundary settings unreadable by non-boundary settings at any inlining depth,
            // including the sealed-dispatch path that writes no per-type id of its own. Byte stability
            // for callers who mark NO boundary is pinned separately by BoundaryWriteDriftTests.
            BoundaryWriteDriftTests.WriteHex(withUnrelatedBoundary, graph)
                .Should().NotBe(BoundaryWriteDriftTests.WriteHex(plainSettings, graph));
        }

        [Theory]
        [MemberData(nameof(SettingsVariants))]
        public void MarkingDifferentBoundaryTypesProducesDifferentVersionIds(bool flattenClassHierarchy, int inliningMaxDepth)
        {
            Settings Configure(Settings s, Type boundary)
            {
                s.FlattenClassHierarchy = flattenClassHierarchy;
                s.InliningMaxDepth = inliningMaxDepth;
                return s.MarkBoundary(boundary);
            }

            var graph = BoundaryWriteDriftTests.BuildPinnedGraph();

            // Combining the marked type NAMES, not just how many there are: two settings marking
            // different types must not collide, or each would accept the other's payloads.
            BoundaryWriteDriftTests.WriteHex(
                    Configure(BoundaryWriteDriftTests.PinnedSettings(Mode.Graph), typeof(Boundary)), graph)
                .Should().NotBe(BoundaryWriteDriftTests.WriteHex(
                    Configure(BoundaryWriteDriftTests.PinnedSettings(Mode.Graph), typeof(SealedBoundary)), graph));
        }

        [Fact]
        public void BoundarySettingsDoNotDeduplicateOntoPlainSettings()
        {
            var plain = BoundaryWriteDriftTests.PinnedSettings(Mode.Graph).ToImmutable();
            var withBoundary = BoundaryWriteDriftTests.PinnedSettings(Mode.Graph)
                .MarkBoundary<Boundary>()
                .ToImmutable();

            withBoundary.Should().NotBeSameAs(plain);
            withBoundary.GetHashCode().Should().NotBe(plain.GetHashCode());
        }

        [Fact]
        public void DeduplicatorDistinguishesSettingsThatDifferOnlyInTheirBoundarySet()
        {
            var plain = BoundaryWriteDriftTests.PinnedSettings(Mode.Graph).ToImmutable();
            var withBoundary = BoundaryWriteDriftTests.PinnedSettings(Mode.Graph)
                .MarkBoundary<Boundary>()
                .ToImmutable();

            // Asserted against the comparator directly: the hash-inequality check above short-circuits
            // the dictionary lookup, so only this reaches ImmutableSettingsDeduplicator.Equals.
            new ImmutableSettingsDeduplicator().Equals(plain, withBoundary).Should().BeFalse();
        }

        [Fact]
        public void DeduplicatorMatchesSettingsWithEqualBoundarySets()
        {
            var first = BoundaryWriteDriftTests.PinnedSettings(Mode.Graph).MarkBoundary<Boundary>().ToImmutable();
            var second = BoundaryWriteDriftTests.PinnedSettings(Mode.Graph)
                .MarkBoundary(typeof(Boundary))
                .ToImmutable();

            new ImmutableSettingsDeduplicator().Equals(first, second).Should().BeTrue();
        }

        [Fact]
        public void IdenticalBoundarySettingsDeduplicateOntoEachOther()
        {
            var first = BoundaryWriteDriftTests.PinnedSettings(Mode.Graph).MarkBoundary<Boundary>().ToImmutable();
            var second = BoundaryWriteDriftTests.PinnedSettings(Mode.Graph).MarkBoundary<Boundary>().ToImmutable();

            second.Should().BeSameAs(first);
        }
    }
}
