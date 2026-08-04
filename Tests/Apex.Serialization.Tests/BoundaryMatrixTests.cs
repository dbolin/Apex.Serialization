using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// Drives the boundary read/write paths across the whole settings matrix the base class builds, so
    /// the inlining-depth, hierarchy-flattening, readonly-field and version-id variants are all covered
    /// rather than only the default configuration.
    /// </summary>
    public class BoundaryMatrixTests : AbstractSerializerTestBase
    {
        public class MatrixBoundary
        {
            public int Id;
            public string? Payload;
        }

        public sealed class MatrixSibling
        {
            public int Number;
            public readonly string Text;

            public MatrixSibling(string text)
            {
                Text = text;
            }
        }

        public abstract class MatrixAbstractBoundary
        {
            public int Id;
        }

        public sealed class MatrixConcreteBoundary : MatrixAbstractBoundary
        {
            public string? Extra;
        }

        public struct MatrixStructWithBoundary
        {
            public MatrixBoundary? Inner;
            public int Number;
        }

        public sealed class MatrixHolder
        {
            public MatrixBoundary? First;
            public MatrixBoundary? Second;
            public object? Polymorphic;
            public MatrixBoundary[]? Array;
            public List<MatrixBoundary>? List;
            public MatrixStructWithBoundary Struct;
            public MatrixAbstractBoundary? ViaAbstractBase;
            public MatrixSibling? Sibling;
            public int Value;
        }

        public static readonly MatrixBoundary Substitute = new MatrixBoundary { Id = 99, Payload = "substitute" };

        public static readonly MatrixConcreteBoundary AbstractSubstitute = new MatrixConcreteBoundary { Id = 98, Extra = "abstract" };

        public static Type[] SerializableTypes() => new[] { typeof(List<>) };

        [Fact]
        public void BoundaryFieldsResolveToTheSubstituteAcrossAllSettings()
        {
            _modifySettings = s => s.MarkBoundary<MatrixBoundary>().MarkBoundary<MatrixAbstractBoundary>();
            _setupSerializerGraph = s =>
            {
                ((IBinary)s).SetBoundarySubstitute(Substitute);
                ((IBinary)s).SetBoundarySubstitute(typeof(MatrixAbstractBoundary), AbstractSubstitute);
            };

            var shared = new MatrixBoundary { Id = 1, Payload = "written" };
            var original = new MatrixHolder
            {
                First = shared,
                Second = shared,
                Polymorphic = new MatrixBoundary { Id = 2 },
                // The array read path re-implements the reference-slot handling independently of the
                // field path, so it needs its own coverage.
                Array = new[] { new MatrixBoundary { Id = 3 }, shared },
                List = new List<MatrixBoundary> { new MatrixBoundary { Id = 4 } },
                Struct = new MatrixStructWithBoundary { Inner = new MatrixBoundary { Id = 5 }, Number = 6 },
                ViaAbstractBase = new MatrixConcreteBoundary { Id = 7, Extra = "written" },
                Sibling = new MatrixSibling("sibling") { Number = 5 },
                Value = 42,
            };

            RoundTrip(original, (o, loaded) =>
            {
                loaded.First.Should().BeSameAs(Substitute);
                loaded.Second.Should().BeSameAs(Substitute);
                loaded.Polymorphic.Should().BeSameAs(Substitute);
                loaded.Array.Should().HaveCount(2).And.OnlyContain(x => ReferenceEquals(x, Substitute));
                loaded.List.Should().HaveCount(1).And.OnlyContain(x => ReferenceEquals(x, Substitute));
                loaded.Struct.Inner.Should().BeSameAs(Substitute);
                loaded.Struct.Number.Should().Be(6);
                loaded.ViaAbstractBase.Should().BeSameAs(AbstractSubstitute);
                loaded.Value.Should().Be(42);
                loaded.Sibling!.Number.Should().Be(5);
                loaded.Sibling.Text.Should().Be("sibling");
            }, s => s.SerializationMode == Mode.Graph);
        }
    }
}
