using FluentAssertions;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// A reference in the data is the position, counting from 1, of a type or object read earlier in the same payload.
    /// </summary>
    public class ReferenceIndexTests
    {
        public sealed class Empty
        {
        }

        private static IBinary Serializer(Mode mode) =>
            Binary.Create(new Settings { SerializationMode = mode, UseSerializedVersionId = false }.MarkSerializable(_ => true));

        /// <summary>
        /// Elements are written last to first, so the first element is written last, as its null flag and a reference to
        /// the type the second element's id introduced.
        /// </summary>
        [Fact]
        public void ATypeReferenceToTheLastTypeReadReadsAndOnePastItOrZeroIsRejected()
        {
            using var binary = Serializer(Mode.Tree);
            var payload = Written(binary, new object?[] { new Empty(), new Empty() });
            LastInt(payload).Should().Be(1, "one type has been read when the reference is");

            ReadWithLastIntAs(binary, payload, 2).Should().Throw<InvalidDataException>().WithMessage("*refers to type 2*");
            ReadWithLastIntAs(binary, payload, 0).Should().Throw<InvalidDataException>().WithMessage("*refers to type 0*");
            binary.Read<object?[]>(new MemoryStream(payload)).Should().HaveCount(2).And.AllBeOfType<Empty>();
        }

        /// <summary>
        /// In graph mode the first element is written last as a reference to the object the second element introduced,
        /// which is the last object read by then.
        /// </summary>
        [Fact]
        public void AnObjectReferenceToTheLastObjectReadReadsAndOnePastItOrZeroIsRejected()
        {
            using var binary = Serializer(Mode.Graph);
            var shared = new Empty();
            var payload = Written(binary, new object?[] { shared, shared });
            LastInt(payload).Should().Be(2, "the array and one element have been read when the reference is");

            ReadWithLastIntAs(binary, payload, 3).Should().Throw<InvalidDataException>().WithMessage("*refers to object 3*");
            ReadWithLastIntAs(binary, payload, 0).Should().Throw<InvalidDataException>().WithMessage("*refers to object 0*");
            var read = binary.Read<object?[]>(new MemoryStream(payload));
            read[0].Should().BeOfType<Empty>().And.BeSameAs(read[1]);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(20)]
        public void AGraphReadLetsGoOfTheObjectsItRead(int count)
        {
            using var binary = Serializer(Mode.Graph);
            var payload = Written(binary, Enumerable.Range(0, count).Select(_ => new Empty()).ToArray());

            for (var reads = 1; reads <= 2; ++reads)
            {
                var read = ReadAndDrop(binary, payload);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                read.Where(x => x.TryGetTarget(out _)).Should().BeEmpty("the serializer, still alive, keeps nothing it read, after {0} reads", reads);
            }
        }

        [Fact]
        public void AGraphWriteLetsGoOfTheObjectsItWroteOnASerializerWithAnInternedObject()
        {
            var serializers = new List<IBinary>();
            var written = new List<WeakReference<object>>();
            for (var laterCount = 1; laterCount <= 1024; laterCount *= 4)
            {
                var binary = Serializer(Mode.Graph);
                serializers.Add(binary);
                binary.Intern(new Empty());
                written.AddRange(WriteAndDrop(binary, 256));
                written.AddRange(WriteAndDrop(binary, laterCount));
                written.AddRange(WriteAndDrop(binary, laterCount));
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            written.Where(x => x.TryGetTarget(out _)).Should().BeEmpty("the serializers, still alive, keep nothing they wrote");
            foreach (var binary in serializers)
            {
                binary.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference<object>[] WriteAndDrop(IBinary binary, int count)
        {
            var value = Enumerable.Range(0, count).Select(_ => new Empty()).ToArray();
            Written(binary, value);
            return value.Prepend<object>(value).Select(x => new WeakReference<object>(x)).ToArray();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference<object>[] ReadAndDrop(IBinary binary, byte[] payload)
        {
            var read = binary.Read<Empty[]>(new MemoryStream(payload));
            read.Should().OnlyContain(x => x != null);
            return read.Prepend<object>(read).Select(x => new WeakReference<object>(x)).ToArray();
        }

        private static Func<object?[]> ReadWithLastIntAs(IBinary binary, byte[] payload, int value)
        {
            var mutated = payload.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(mutated.AsSpan(mutated.Length - sizeof(int)), value);
            return () => binary.Read<object?[]>(new MemoryStream(mutated));
        }

        private static int LastInt(byte[] payload) => BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(payload.Length - sizeof(int)));

        private static byte[] Written<T>(IBinary binary, T value)
        {
            using var written = new MemoryStream();
            binary.Write(value, written);
            return written.ToArray();
        }
    }
}
