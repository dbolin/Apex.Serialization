using FluentAssertions;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Xunit;

namespace Apex.Serialization.Tests
{
    /// <summary>
    /// Reading recurses once per level of nesting, so data nested deeper than the reading thread's stack can hold has to
    /// end in an exception the caller can catch, not in a stack overflow that ends the process.  The check skips the
    /// runtime's call only on native 64-bit Windows, so only there do these tests exercise that fast path; elsewhere
    /// every level calls the runtime's check.
    /// </summary>
    public class NestingDepthTests
    {
        public sealed class Link
        {
            public Link? Next;
        }

        public class BaseLink
        {
            public int Index;
        }

        /// <summary>
        /// With the hierarchy not flattened, each level also reads its base class's fields through a read of their own.
        /// </summary>
        public sealed class DerivedLink : BaseLink
        {
            public DerivedLink? Next;
        }

        /// <summary>
        /// Written and read by custom code that nests one value inside the next, so reading it recurses through the reads
        /// of value types alone.
        /// </summary>
        public struct Chain
        {
            public int Depth;
        }

        /// <summary>
        /// A sealed class that custom code nests the same way, so its levels read through a custom reader's
        /// <c>ReadObject</c>.
        /// </summary>
        public sealed class Node
        {
            public int Depth;
        }

        public static class Handlers
        {
            public static void First() { }

            public static void Second() { }
        }

        private const int HostileDepth = 100_000;

        // The deepest the data of the library's main consumer nests.
        private const int DeepestRealDepth = 300;

        // On a 1 MiB stack the shapes here read between about 1,400 (Node, Debug) and 19,000 (Links, Release) levels,
        // measured on .NET 10 x64, so twice the real depth leaves room for frames that grow.
        private const int ReadableDepth = 2 * DeepestRealDepth;

        private const int ReaderStackBytes = 1024 * 1024;

        private const int WriterStackBytes = 1024 * 1024 * 1024;

        private static Settings NestingSettings(Action? onChainRead = null) => new Settings { AllowFunctionSerialization = true, SupportSerializationHooks = true }
            .MarkSerializable(_ => true)
            .RegisterCustomSerializer<Chain>((chain, writer) =>
            {
                writer.Write(chain.Depth);
                if (chain.Depth > 1)
                {
                    writer.WriteObject(new Chain { Depth = chain.Depth - 1 });
                }
            }, (_, _) => { })
            .RegisterCustomInstantiator<Chain>(reader =>
            {
                onChainRead?.Invoke();
                var depth = reader.Read<int>();
                if (depth > 1)
                {
                    reader.ReadObject<Chain>();
                }

                return new Chain { Depth = depth };
            })
            .RegisterCustomSerializer<Node>((node, writer) =>
            {
                writer.Write(node.Depth);
                if (node.Depth > 1)
                {
                    writer.WriteObject(new Node { Depth = node.Depth - 1 });
                }
            }, (_, _) => { })
            .RegisterCustomInstantiator<Node>(reader =>
            {
                var depth = reader.Read<int>();
                if (depth > 1)
                {
                    reader.ReadObject<Node>();
                }

                return new Node { Depth = depth };
            });

        /// <summary>
        /// Three quarters of the fewest levels each shape read on a 1 MiB stack, measured on .NET 10 x64 with tiered
        /// compilation warmed up, off, and stopped at tier 0, rounded down to a multiple of
        /// <see cref="DeepestRealDepth"/>.  A change that widens the frame of every level fails here.
        /// </summary>
        public static TheoryData<bool, string, int> DepthFloors() => new TheoryData<bool, string, int>
        {
#if DEBUG
            { false, nameof(Links), 17 },
            { false, nameof(ObjectArrays), 7 },
            { false, nameof(Chain), 3 },
            { false, nameof(Node), 3 },
            { true, nameof(Links), 9 },
            { true, nameof(DerivedLinks), 8 },
#else
            { false, nameof(Links), 47 },
            { false, nameof(ObjectArrays), 10 },
            { false, nameof(Chain), 5 },
            { false, nameof(Node), 4 },
            { true, nameof(Links), 23 },
            { true, nameof(DerivedLinks), 8 },
#endif
        };

        /// <summary>
        /// Graph mode, the hierarchy not flattened, no inlining, hooks and version ids: a large consumer's settings.
        /// </summary>
        private static Settings GraphSettings() => new Settings
        {
            SerializationMode = Mode.Graph,
            FlattenClassHierarchy = false,
            InliningMaxDepth = 0,
            SupportSerializationHooks = true,
            AllowFunctionSerialization = true,
        }.MarkSerializable(_ => true);

        public static TheoryData<string> Shapes() => new TheoryData<string> { nameof(ObjectArrays), nameof(Links), nameof(Chain), nameof(Node) };

        private static object Nest(string shape, int depth) => shape switch
        {
            nameof(ObjectArrays) => ObjectArrays(depth),
            nameof(Links) => Links(depth),
            nameof(Chain) => new Chain { Depth = depth },
            nameof(Node) => new Node { Depth = depth },
            nameof(DerivedLinks) => DerivedLinks(depth),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        private static object?[] ObjectArrays(int depth)
        {
            var root = new object?[1];
            var current = root;
            for (var i = 1; i < depth; ++i)
            {
                var next = new object?[1];
                current[0] = next;
                current = next;
            }

            return root;
        }

        private static DerivedLink DerivedLinks(int depth)
        {
            var root = new DerivedLink();
            var current = root;
            for (var i = 1; i < depth; ++i)
            {
                current.Next = new DerivedLink { Index = i };
                current = current.Next;
            }

            return root;
        }

        private static Link Links(int depth)
        {
            var root = new Link();
            var current = root;
            for (var i = 1; i < depth; ++i)
            {
                current.Next = new Link();
                current = current.Next;
            }

            return root;
        }

        private static int Depth(object? value) => value switch
        {
            object?[] array => 1 + Depth(array[0]),
            Link link => 1 + Depth(link.Next),
            Chain chain => chain.Depth,
            Node node => node.Depth,
            DerivedLink link => 1 + Depth(link.Next),
            _ => 0,
        };

        [Theory]
        [MemberData(nameof(Shapes))]
        public void DataNestedFarDeeperThanRealDataThrowsACatchableException(string shape)
        {
            var hostile = Write(Nest(shape, HostileDepth));
            var real = Write(Nest(shape, DeepestRealDepth));

            var (exception, _, readAfter) = ReadOnSmallStack(hostile, real);

            exception.Should().BeOfType<InsufficientExecutionStackException>();
            Depth(readAfter).Should().Be(DeepestRealDepth, "the serializer reads intact data after the exception");
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void DataNestedTwiceAsDeepAsRealDataReads(string shape)
        {
            var deep = Write(Nest(shape, ReadableDepth));

            var (exception, read, _) = ReadOnSmallStack(deep);

            exception.Should().BeNull();
            Depth(read).Should().Be(ReadableDepth);
        }

        [Theory]
        [MemberData(nameof(DepthFloors))]
        public void EachShapeReadsAtLeastItsDepthFloor(bool graph, string shape, int multiple)
        {
            var depth = multiple * DeepestRealDepth;
            var settings = graph ? (Func<Settings>)GraphSettings : () => NestingSettings();
            var deep = Write(Nest(shape, depth), settings);

            var (exception, read, _) = ReadOnSmallStack(deep, settings: settings);

            exception.Should().BeNull();
            Depth(read).Should().Be(depth);
        }

        [Fact]
        public void NoLevelOfNestedDataStartsReadingWhereTheRuntimesStackCheckWouldRefuse()
        {
            var hostile = Write(Nest(nameof(Chain), HostileDepth));
            var levelsRead = 0;
            var levelsTheRuntimeWouldRefuse = 0;
            Exception? exception = null;
            var reader = new Thread(() =>
            {
                using var binary = Binary.Create(NestingSettings(() =>
                {
                    ++levelsRead;
                    if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
                    {
                        ++levelsTheRuntimeWouldRefuse;
                    }
                }));
                try
                {
                    binary.Read<object>(new MemoryStream(hostile));
                }
                catch (Exception e)
                {
                    exception = e;
                }
            }, ReaderStackBytes);
            reader.Start();
            reader.Join();

            exception.Should().BeOfType<InsufficientExecutionStackException>();
            // The instantiator runs a few frames below the check that admitted its level, so the deepest admitted level
            // can be just past the runtime's margin; any level after it would have been refused.
            levelsTheRuntimeWouldRefuse.Should().BeLessThanOrEqualTo(1);
            levelsRead.Should().BeGreaterThan(ReadableDepth);
        }

        [Fact]
        public void ASealedChainReadThroughACustomReaderAfterAnIntactReadThrowsACatchableException()
        {
            var intact = Write(new Node { Depth = DeepestRealDepth });
            var hostile = Write(new Node { Depth = HostileDepth });
            var depths = new List<int>();
            Exception? exception = null;
            var reader = new Thread(() =>
            {
                using var binary = Binary.Create(NestingSettings());
                // The intact read leaves the root's read cached, so the hostile root goes through the cached path.
                depths.Add(binary.Read<Node>(new MemoryStream(intact)).Depth);
                try
                {
                    binary.Read<Node>(new MemoryStream(hostile));
                }
                catch (Exception e)
                {
                    exception = e;
                }

                depths.Add(binary.Read<Node>(new MemoryStream(intact)).Depth);
            }, ReaderStackBytes);
            reader.Start();
            reader.Join();

            exception.Should().BeOfType<InsufficientExecutionStackException>();
            depths.Should().Equal(DeepestRealDepth, DeepestRealDepth);
        }

        [Fact]
        public void AMulticastDelegateNestedFarDeeperThanRealDataThrowsACatchableException()
        {
            var real = Write<object>((Action)Handlers.First + Handlers.Second);

            var (exception, _, readAfter) = ReadOnSmallStack(NestedInvocationLists(real, HostileDepth), real);

            exception.Should().BeOfType<InsufficientExecutionStackException>();
            ((Action)readAfter!).GetInvocationList().Select(x => x.Method.Name).Should().Equal(nameof(Handlers.First), nameof(Handlers.Second));
        }

        /// <summary>
        /// Rewrites the payload of a two-entry multicast delegate so that its invocation list holds one entry whose own
        /// list holds one entry, and so on <paramref name="depth"/> times, which no delegate the writer is given produces.
        /// </summary>
        private static byte[] NestedInvocationLists(byte[] twoEntries, int depth)
        {
            // An entry is the indices of its delegate's and method's types, its method name, its parameter and generic
            // argument counts, its has-target flag, its multicast flag and its invocation count.
            var name = Encoding.Unicode.GetBytes(nameof(Handlers.First));
            var nameAt = twoEntries.AsSpan().IndexOf(name);
            var entryAt = nameAt - 3 * sizeof(int);
            var entryLength = 3 * sizeof(int) + name.Length + 4 + sizeof(int);
            var entry = twoEntries.AsSpan(entryAt, entryLength).ToArray();
            BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(2 * sizeof(int))).Should().Be(nameof(Handlers.First).Length);
            BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(entryLength - sizeof(int))).Should().Be(0);
            BinaryPrimitives.ReadInt32LittleEndian(twoEntries.AsSpan(entryAt - sizeof(int))).Should().Be(2);

            var nested = entry.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(nested.AsSpan(entryLength - sizeof(int)), 1);
            var oneEntry = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(oneEntry, 1);

            using var payload = new MemoryStream();
            payload.Write(twoEntries, 0, entryAt - sizeof(int));
            payload.Write(oneEntry);
            for (var i = 0; i < depth; ++i)
            {
                payload.Write(nested);
            }

            payload.Write(entry);
            return payload.ToArray();
        }

        private static byte[] Write<T>(T value, Func<Settings>? settings = null)
        {
            byte[]? payload = null;
            var writer = new Thread(() =>
            {
                using var binary = Binary.Create(settings?.Invoke() ?? NestingSettings());
                using var stream = new MemoryStream();
                binary.Write(value, stream);
                payload = stream.ToArray();
            }, WriterStackBytes);
            writer.Start();
            writer.Join();
            return payload!;
        }

        private static (Exception? Exception, object? Read, object? ReadAfter) ReadOnSmallStack(byte[] payload, byte[]? intact = null,
            Func<Settings>? settings = null)
        {
            Exception? exception = null;
            object? read = null;
            object? readAfter = null;
            var reader = new Thread(() =>
            {
                using var binary = Binary.Create(settings?.Invoke() ?? NestingSettings());
                try
                {
                    read = binary.Read<object>(new MemoryStream(payload));
                }
                catch (Exception e)
                {
                    exception = e;
                }

                if (intact != null)
                {
                    readAfter = binary.Read<object>(new MemoryStream(intact));
                }
            }, ReaderStackBytes);
            reader.Start();
            reader.Join();
            return (exception, read, readAfter);
        }
    }
}
