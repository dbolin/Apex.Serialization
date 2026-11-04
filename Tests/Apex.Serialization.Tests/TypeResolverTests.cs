using Apex.Serialization.Internal;
using FluentAssertions;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class TypeResolverTests
    {
        public sealed class Widget
        {
            public int Value;
        }

        private static readonly string TestAssemblyName = typeof(Widget).Assembly.FullName!;

        private static readonly string WidgetName = TypeResolver.CanonicalName(typeof(Widget));

        // Loaded by nothing else the tests run, and in the shared framework, so in the trusted platform assemblies list.
        private const string UnloadedFrameworkAssembly = "System.Formats.Tar";

        private const string AssemblyOutsideTheTrustedList = "Apex.Serialization.Tests.NotATrustedAssembly";

        public enum ByteTag : byte
        {
            Directory = 53,
        }

        // Two loaded assemblies with the same simple name, defined once per process.
        private static readonly Lazy<Type[]> Twins = new Lazy<Type[]>(() => new[] { DefineTwin(1), DefineTwin(2) });

        private static Type DefineTwin(int major)
        {
            var name = new AssemblyName("ApexResolverTwins") { Version = new Version(major, 0, 0, 0) };
            var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            return assembly.DefineDynamicModule(name.Name!).DefineType("Twin", TypeAttributes.Public).CreateType()!;
        }

        private static string WithAssembly(Type type, string assemblyName) => type.FullName + ", " + assemblyName;

        private static string WithVersion(string assemblyName, Version version) => new AssemblyName(assemblyName) { Version = version }.FullName;

        [Fact]
        public void ACanonicalNameResolvesToItsType()
        {
            TypeResolver.Find(WidgetName, out var isCanonical).Should().Be(typeof(Widget));
            isCanonical.Should().BeTrue();
        }

        [Fact]
        public void AnAssemblyNameMatchingOnlyALoadedAssemblysSimpleNameResolvesButIsntCanonical()
        {
            var spellings = new[]
            {
                WithVersion(TestAssemblyName, new Version(99, 0, 0, 0)),
                typeof(Widget).Assembly.GetName().Name!,
                typeof(Widget).Assembly.GetName().Name!.ToUpperInvariant(),
            };

            foreach (var spelling in spellings)
            {
                TypeResolver.Find(WithAssembly(typeof(Widget), spelling), out var isCanonical).Should().Be(typeof(Widget), spelling);
                isCanonical.Should().BeFalse(spelling);
            }
        }

        [Fact]
        public void AnAssemblyWhoseFullNameMatchesIsPreferredOverOneWithTheSameSimpleName()
        {
            foreach (var twin in Twins.Value)
            {
                TypeResolver.Find(TypeResolver.CanonicalName(twin), out var isCanonical).Should().BeSameAs(twin);
                isCanonical.Should().BeTrue();
            }
        }

        [Fact]
        public void AValueOfATypeFromAFrameworkAssemblyNothingHasLoadedReads()
        {
            LoadedAssemblyNames().Should().NotContain(UnloadedFrameworkAssembly, "the test needs an assembly nothing has loaded");
            using var binary = Serializer();
            WriteObject(ByteTag.Directory).Should().Equal(ObjectPayload(TypeId(typeof(ByteTag).FullName!, ", ", TestAssemblyName), (byte)ByteTag.Directory),
                "the test spells a byte enum written as an object the way the writer does");

            // Named, never referenced, so that compiling this test doesn't load the assembly.
            var read = binary.Read<object>(new MemoryStream(ObjectPayload(TypeId("System.Formats.Tar.TarEntryType", UnloadedFrameworkAssembly), (byte)ByteTag.Directory)));

            read.GetType().FullName.Should().Be("System.Formats.Tar.TarEntryType");
            read.GetType().Assembly.GetName().Name.Should().Be(UnloadedFrameworkAssembly);
            read.ToString().Should().Be("Directory");
        }

        [Fact]
        public void ATypeNamingAnAssemblyOutsideTheTrustedPlatformListDoesntResolveOrAskTheRuntimeToLoadIt()
        {
            TrustedPlatformAssemblyNames().Should().NotContain(AssemblyOutsideTheTrustedList);
            var requested = new ConcurrentQueue<string?>();
            Func<AssemblyLoadContext, AssemblyName, Assembly?> record = (_, name) =>
            {
                requested.Enqueue(name.Name);
                return null;
            };
            AssemblyLoadContext.Default.Resolving += record;
            try
            {
                TypeResolver.Find("Some.Type, " + AssemblyOutsideTheTrustedList, out _).Should().BeNull();
            }
            finally
            {
                AssemblyLoadContext.Default.Resolving -= record;
            }

            requested.Should().NotContain(AssemblyOutsideTheTrustedList);
            LoadedAssemblyNames().Should().NotContain(AssemblyOutsideTheTrustedList);
        }

        public static TheoryData<Type> ByRefAndPointerTypes() => new TheoryData<Type>
        {
            typeof(int).MakeByRefType(),
            typeof(Widget).MakeByRefType(),
            typeof(int).MakePointerType(),
            typeof(int).MakePointerType().MakeArrayType(),
            typeof(int).MakePointerType().MakeByRefType(),
        };

        [Theory]
        [MemberData(nameof(ByRefAndPointerTypes))]
        public void AByRefOrPointerTypeResolvesFromItsCanonicalName(Type type)
        {
            TypeResolver.Find(TypeResolver.CanonicalName(type), out var isCanonical).Should().Be(type);
            isCanonical.Should().BeTrue();
        }

        private static readonly string CoreLibrary = typeof(int).Assembly.FullName!;

        private static string InCoreLibrary(string typeName) => typeName + ", " + CoreLibrary;

        public static TheoryData<string> NamesTheRuntimeCantMakeATypeOf() => new TheoryData<string>
        {
            "System.Int32&&",
            "System.Int32&*",
            "System.Int32&[]",
            "System.Void[]",
            $"System.Span`1[[{InCoreLibrary("System.Int32")}]][]",
            "System.Int32[" + new string(',', 32) + "]",
            $"System.Collections.Generic.List`1[[{InCoreLibrary("System.Int32&")}]]",
            $"System.Collections.Generic.List`1[[{InCoreLibrary("System.Int32")}],[{InCoreLibrary("System.Int32")}]]",
            $"System.Nullable`1[[{InCoreLibrary("System.String")}]]",
            $"System.Int32[[{InCoreLibrary("System.Int32")}]]",
        };

        [Theory]
        [MemberData(nameof(NamesTheRuntimeCantMakeATypeOf))]
        public void ANameTheRuntimeCantMakeATypeOfResolvesToNothing(string typeName)
        {
            var name = InCoreLibrary(typeName);
            TypeName.TryParse(name, out _).Should().BeTrue("the resolver only tries making the type of a name that parses");

            TypeResolver.Find(name, out var isCanonical).Should().BeNull();
            isCanonical.Should().BeFalse();
        }

        public static TheoryData<string> TypesNoObjectIsReadAs() => new TheoryData<string>
        {
            "System.Int32&",
            "System.Int32*",
            "System.Int32*[]",
            "System.TypedReference",
            $"System.Span`1[[{InCoreLibrary("System.Int32")}]]",
            "System.Void",
            "System.Collections.Generic.List`1",
            "System.Collections.Generic.List`1[]",
        };

        [Theory]
        [MemberData(nameof(TypesNoObjectIsReadAs))]
        public void AnObjectStoredAsATypeNoObjectIsReadAsIsRejected(string typeName)
        {
            using var binary = Binary.Create(new Settings { UseSerializedVersionId = false }.MarkSerializable(_ => true));
            var payload = ObjectPayload(TypeId(typeName, CoreLibrary), new byte[16]);

            var read = () => binary.Read<object>(new MemoryStream(payload));

            read.Should().Throw<InvalidDataException>();
            binary.Read<object>(new MemoryStream(WriteWidget())).Should().BeOfType<Widget>();
        }

        [Fact]
        public void ANameWithMoreNodesThanTheParsersDefaultAllowsResolves()
        {
            var type = typeof(Func<(int, int, int, int), (int, int, int, int), (int, int, int, int), int>);
            var name = TypeResolver.CanonicalName(type);
            TypeName.TryParse(name, out _).Should().BeFalse("the parser's default node limit rejects the name");

            TypeResolver.Find(name, out _).Should().Be(type);
        }

        [Fact]
        public void ANameNestedTenThousandDeepResolvesToNothingOnASmallStack()
        {
            const int depth = 10_000;
            var name = InCoreLibrary(string.Concat(Enumerable.Repeat("System.Collections.Generic.List`1[[", depth)) + "System.Int32"
                + string.Concat(Enumerable.Repeat(", " + CoreLibrary + "]]", depth)));
            Type? resolved = typeof(object);
            var reader = new Thread(() => resolved = TypeResolver.Find(name, out _), 1024 * 1024);

            reader.Start();
            reader.Join();

            resolved.Should().BeNull();
        }

        [Fact]
        public void ATypeTheNamedAssemblyDoesntDeclareDoesntResolveFromAnotherAssembly()
        {
            TypeResolver.Find(WithAssembly(typeof(string), TestAssemblyName), out _).Should().BeNull();
        }

        [Theory]
        [InlineData("Apex.Serialization.Tests.TypeResolverTests+Widget")]
        [InlineData("")]
        [InlineData(",")]
        [InlineData("Widget, ")]
        [InlineData(", Apex.Serialization.Tests")]
        [InlineData("System.Int32[[, System.Private.CoreLib")]
        public void ANameThatIsntAnAssemblyQualifiedTypeNameResolvesToNothing(string name)
        {
            TypeResolver.Find(name, out var isCanonical).Should().BeNull();
            isCanonical.Should().BeFalse();
        }

        [Fact]
        public void ReadingAStoredTypeNameWithNoCommaFailsAsAnUnresolvableType()
        {
            using var binary = Serializer();
            var noComma = WidgetPayload(TypeId(typeof(Widget).FullName!, "  ", ""));

            var read = () => binary.Read<object>(new MemoryStream(noComma));

            read.Should().Throw<InvalidOperationException>().WithMessage("Unable to resolve type*");
            binary.Read<object>(new MemoryStream(WidgetPayload(TypeId(typeof(Widget).FullName!, ", ", TestAssemblyName)))).Should().BeOfType<Widget>();
        }

        [Fact]
        public void ReadingATypeSpelledManyWaysCachesOneEntryForIt()
        {
            using var binary = Serializer();
            var canonical = WidgetPayload(TypeId(typeof(Widget).FullName!, ", ", TestAssemblyName));
            WriteWidget().Should().Equal(canonical, "the test spells type ids the way the writer does");
            var spellings = new[]
            {
                canonical,
                WidgetPayload(TypeId(typeof(Widget).FullName!, ", ", WithVersion(TestAssemblyName, new Version(99, 0, 0, 0)))),
                WidgetPayload(TypeId(typeof(Widget).FullName!, ", ", WithVersion(TestAssemblyName, new Version(7, 7, 7, 7)))),
                WidgetPayload(TypeId(typeof(Widget).FullName!, ", ", typeof(Widget).Assembly.GetName().Name!)),
            };

            foreach (var payload in spellings.Concat(spellings))
            {
                binary.Read<object>(new MemoryStream(payload)).Should().BeOfType<Widget>().Which.Value.Should().Be(WidgetValue);
            }

            CachedNames(typeof(Widget)).Should().Equal(WidgetName);
            CachedIdCount(binary, typeof(Widget)).Should().Be(1);
        }

        private const int WidgetValue = 7;

        private static IBinary Serializer() => Binary.Create(new Settings { UseSerializedVersionId = false }.MarkSerializable(typeof(Widget)));

        private static byte[] WriteWidget() => WriteObject(new Widget { Value = WidgetValue });

        private static byte[] WriteObject(object value)
        {
            using var binary = Serializer();
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }

        private static byte[] WidgetPayload(byte[] typeId)
        {
            var value = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(value, WidgetValue);
            return ObjectPayload(typeId, value);
        }

        /// <summary>
        /// A value written as an object: its null flag, the -1 that marks a type named in full, the type id, then the
        /// value's fields.
        /// </summary>
        private static byte[] ObjectPayload(byte[] typeId, params byte[] fields)
        {
            var payload = new byte[1 + sizeof(int) + typeId.Length + fields.Length];
            payload[0] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1), -1);
            typeId.CopyTo(payload, 1 + sizeof(int));
            fields.CopyTo(payload, 1 + sizeof(int) + typeId.Length);
            return payload;
        }

        internal static byte[] TypeId(Type type) => TypeId(type.FullName!, ", ", type.Assembly.FullName!);

        internal static byte[] TypeId(string typeName, string assemblyName) => TypeId(typeName, ", ", assemblyName);

        /// <summary>
        /// A type id as the stream holds it: the byte lengths of the type name and the assembly name, then the UTF-8 names,
        /// each with its bytes reordered, around a two-byte separator.
        /// </summary>
        private static byte[] TypeId(string typeName, string separator, string assemblyName)
        {
            var name = Encoding.UTF8.GetBytes(typeName);
            var assembly = Encoding.UTF8.GetBytes(assemblyName);
            Reorder(name);
            Reorder(assembly);

            using var id = new MemoryStream();
            var lengths = new byte[2 * sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(lengths, name.Length);
            BinaryPrimitives.WriteInt32LittleEndian(lengths.AsSpan(sizeof(int)), assembly.Length);
            id.Write(lengths);
            id.Write(name);
            id.Write(Encoding.UTF8.GetBytes(separator));
            id.Write(assembly);
            return id.ToArray();
        }

        private static void Reorder(Span<byte> bytes)
        {
            for (int i = 0, length = bytes.Length; i < length; i += 2, length -= 2)
            {
                (bytes[i], bytes[length - 1]) = (bytes[length - 1], bytes[i]);
            }
        }

        private static IEnumerable<string?> LoadedAssemblyNames() => AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetName().Name);

        private static IEnumerable<string> TrustedPlatformAssemblyNames() =>
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(Path.GetFileNameWithoutExtension)!;

        private static IEnumerable<string> CachedNames(Type type)
        {
            var cache = (ConcurrentDictionary<string, Type>)typeof(TypeResolver).GetField("_canonicalNames", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            return cache.Where(x => x.Value == type).Select(x => x.Key);
        }

        private static int CachedIdCount(IBinary binary, Type type)
        {
            var lookup = (TypeLookup)binary.GetType().GetField("_knownTypes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(binary)!;
            var ids = (Dictionary<byte[], Type>)typeof(TypeLookup).GetField("_types", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(lookup)!;
            return ids.Values.Count(x => x == type);
        }
    }
}
