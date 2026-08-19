using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Serialization.Internal;
using Apex.Serialization.Internal.Reflection;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class PrecompileVariantsTests
    {
        // These types must stay unique to this test class: the delegate caches asserted against are
        // process-wide, so a type also serialized by another test would make presence/absence and
        // key-count assertions here race that test.
        public abstract class HierarchyBase
        {
            public int BaseValue;
        }

        public class HierarchyMiddle : HierarchyBase
        {
        }

        public sealed class HierarchyDerived : HierarchyMiddle
        {
            public int DerivedValue;
        }

        public abstract class FieldBase
        {
            public int FieldBaseValue;
        }

        public sealed class FieldLeaf : FieldBase
        {
            public int LeafValue;
        }

        public abstract class ChainBase
        {
            public FieldLeaf? Field;
        }

        public sealed class ChainDerived : ChainBase
        {
            public int DerivedValue;
        }

        public abstract class FlattenedHierarchyBase
        {
            public int BaseValue;
        }

        public sealed class FlattenedHierarchyDerived : FlattenedHierarchyBase
        {
            public int DerivedValue;
        }

        public class UnsealedLeaf
        {
            public int Value;
        }

        public class GatedBase
        {
            public int Value;
        }

        public sealed class GatedSub : GatedBase
        {
        }

        public abstract class BoundaryBase
        {
            public int BaseValue;
        }

        public sealed class BoundaryDerived : BoundaryBase
        {
            public int DerivedValue;
        }

        public sealed class ListDerived : List<int>
        {
        }

        public sealed class SelfList : List<SelfList>
        {
        }

        public abstract class CycleBase
        {
            public CycleDerived? Child;
        }

        public sealed class CycleDerived : CycleBase
        {
            public int Value;
        }

        private static IBinary CreateBinary(bool flattenClassHierarchy = true)
        {
            return Binary.Create(new Settings
            {
                FlattenClassHierarchy = flattenClassHierarchy,
            }.MarkSerializable(x => true));
        }

        private static List<TypeKey> KeysFor(IEnumerable<KeyValuePair<TypeKey, DynamicCodeMethods.GeneratedDelegate>> cache, params Type[] types)
        {
            return cache.Select(x => x.Key).Where(k => types.Contains(k.Type))
                .OrderBy(k => k.Type.FullName, StringComparer.Ordinal)
                .ThenBy(k => k.IncludesTypeInfo)
                .ThenBy(k => k.Isolated)
                .ToList();
        }

        private static void AssertNoCodegenAcrossFirstRoundTrip<T>(IBinary binary, T instance, Action<T> verify, params Type[] fixtureTypes)
        {
            // The property Precompile exists to establish: a first write and read after it trigger no
            // further code generation for the fixture's types. Snapshot-compare rather than assert named
            // keys, so ANY lazily-added variant fails — a wrong-key pre-generation, a missed isolated
            // delegate, the UseSerializedVersionId fallback that runs a full Precompile of a base whose
            // VersionUniqueId was left unset, or a nested chain skipped by the reentrancy guard.
            var writeKeys = KeysFor(DynamicCodeMethods._virtualWriteMethods, fixtureTypes);
            var readKeys = KeysFor(DynamicCodeMethods._virtualReadMethods, fixtureTypes);

            using var stream = new MemoryStream();
            binary.Write(instance, stream);
            stream.Seek(0, SeekOrigin.Begin);
            verify(binary.Read<T>(stream));

            KeysFor(DynamicCodeMethods._virtualWriteMethods, fixtureTypes).Should().Equal(writeKeys);
            KeysFor(DynamicCodeMethods._virtualReadMethods, fixtureTypes).Should().Equal(readKeys);
        }

        [Fact]
        public void PrecompilingADerivedTypeLeavesNoCodegenForItsFirstWriteAndRead()
        {
            using var binary = CreateBinary(flattenClassHierarchy: false);
            binary.Precompile(typeof(HierarchyDerived));

            DynamicCodeMethods._virtualWriteMethods.Keys
                .Count(k => k.Type == typeof(HierarchyBase) && k.Isolated && !k.IncludesTypeInfo).Should().Be(1);
            DynamicCodeMethods._virtualReadMethods.Keys
                .Count(k => k.Type == typeof(HierarchyBase) && k.Isolated && !k.IncludesTypeInfo).Should().Be(1);
            DynamicCodeMethods._virtualWriteMethods.Keys
                .Should().NotContain(k => k.Type == typeof(HierarchyMiddle) && k.Isolated);
            DynamicCodeMethods._virtualReadMethods.Keys
                .Should().NotContain(k => k.Type == typeof(HierarchyMiddle) && k.Isolated);

            AssertNoCodegenAcrossFirstRoundTrip(binary,
                new HierarchyDerived { BaseValue = 3, DerivedValue = 4 },
                result =>
                {
                    result.BaseValue.Should().Be(3);
                    result.DerivedValue.Should().Be(4);
                },
                typeof(HierarchyBase), typeof(HierarchyMiddle), typeof(HierarchyDerived));
        }

        [Fact]
        public void PrecompilingCoversABasesOwnFieldHierarchy()
        {
            // The shape a whole-subtree reentrancy guard got wrong: the base's field type has its own base
            // chain, reached only from inside the base's eager generation.
            using var binary = CreateBinary(flattenClassHierarchy: false);
            binary.Precompile(typeof(ChainDerived));

            AssertNoCodegenAcrossFirstRoundTrip(binary,
                new ChainDerived { DerivedValue = 5, Field = new FieldLeaf { FieldBaseValue = 6, LeafValue = 7 } },
                result =>
                {
                    result.DerivedValue.Should().Be(5);
                    result.Field!.FieldBaseValue.Should().Be(6);
                    result.Field.LeafValue.Should().Be(7);
                },
                typeof(ChainDerived), typeof(ChainBase), typeof(FieldLeaf), typeof(FieldBase));
        }

        [Fact]
        public void PrecompilingUnderAFlattenedHierarchyGeneratesNoIsolatedDelegates()
        {
            using var binary = CreateBinary();
            binary.Precompile(typeof(FlattenedHierarchyDerived));

            DynamicCodeMethods._virtualWriteMethods.Keys
                .Should().NotContain(k => k.Type == typeof(FlattenedHierarchyBase) && k.Isolated);
            DynamicCodeMethods._virtualReadMethods.Keys
                .Should().NotContain(k => k.Type == typeof(FlattenedHierarchyBase) && k.Isolated);
        }

        [Fact]
        public void PrecompilingAnUnsealedLeafGeneratesItsSealedVariants()
        {
            // The fixture only means anything while nothing in the test assembly subclasses UnsealedLeaf —
            // fail here, at the precondition, if that changes.
            typeof(UnsealedLeaf).IsSealed.Should().BeFalse();
            StaticTypeInfo.IsSealedOrHasNoDescendents(typeof(UnsealedLeaf)).Should().BeTrue();

            using var binary = CreateBinary(flattenClassHierarchy: false);
            binary.Precompile(typeof(UnsealedLeaf));

            DynamicCodeMethods._virtualWriteMethods.Keys
                .Should().Contain(k => k.Type == typeof(UnsealedLeaf) && !k.IncludesTypeInfo && !k.Isolated);
            DynamicCodeMethods._virtualReadMethods.Keys
                .Should().Contain(k => k.Type == typeof(UnsealedLeaf) && !k.IncludesTypeInfo && !k.Isolated);

            using var stream = new MemoryStream();
            binary.Write(new UnsealedLeaf { Value = 7 }, stream);
            stream.Seek(0, SeekOrigin.Begin);
            binary.Read<UnsealedLeaf>(stream).Value.Should().Be(7);
        }

        [Fact]
        public void PrecompilingATypeWithDescendentsSkipsSealedVariants()
        {
            using var binary = CreateBinary(flattenClassHierarchy: false);
            binary.Precompile(typeof(GatedBase));

            DynamicCodeMethods._virtualWriteMethods.Keys
                .Should().NotContain(k => k.Type == typeof(GatedBase) && !k.IncludesTypeInfo && !k.Isolated);
            DynamicCodeMethods._virtualReadMethods.Keys
                .Should().NotContain(k => k.Type == typeof(GatedBase) && !k.IncludesTypeInfo && !k.Isolated);
        }

        [Fact]
        public void PrecompilingABoundaryTypeNeitherThrowsNorGeneratesBaseDelegates()
        {
            using var binary = Binary.Create(new Settings
            {
                FlattenClassHierarchy = false,
                SerializationMode = Mode.Graph,
            }.MarkSerializable(x => true).MarkBoundary<BoundaryDerived>());

            binary.Precompile(typeof(BoundaryDerived));

            DynamicCodeMethods._virtualWriteMethods.Keys
                .Should().NotContain(k => k.Type == typeof(BoundaryBase) && k.Isolated);
            DynamicCodeMethods._virtualReadMethods.Keys
                .Should().NotContain(k => k.Type == typeof(BoundaryBase) && k.Isolated);
        }

        [Fact]
        public void PrecompilingRecursiveTypeShapesDoesNotOverflow()
        {
            // The shapes where eager base-delegate generation first went reentrant and overflowed the
            // stack: generation of one type reaches a walk that re-enters generation of the same type.
            // Completing at all is this test's assertion.
            using var binary = CreateBinary(flattenClassHierarchy: false);
            binary.Precompile(typeof(ListDerived));
            binary.Precompile(typeof(SelfList));
            binary.Precompile(typeof(CycleDerived));
        }
    }
}
