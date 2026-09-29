using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class FieldOrderTests
    {
        public sealed class ReadonlyOrderProbe<TTag>
        {
            public string? A;
            public readonly string? B = "b";
        }

        public sealed class TagByNameFirst
        {
        }

        public sealed class TagReadonlyFirstFirst
        {
        }

        // The not-null marker, then A ("a") before B ("b"), each as a length and UTF-16 characters.
        private const string ByNameBytes = "01010000006100010000006200";

        // The not-null marker, then B before A: settings that set readonly fields by reflection read those first.
        private const string ReadonlyFirstBytes = "01010000006200010000006100";

        [Fact]
        public void ReadonlyFieldOrderFollowsEachSettingsWhenNameOrderComesFirst()
        {
            ShouldOrderReadonlyFieldsBySettings<TagByNameFirst>(readonlyFirstComesFirst: false);
        }

        [Fact]
        public void ReadonlyFieldOrderFollowsEachSettingsWhenReadonlyFirstOrderComesFirst()
        {
            ShouldOrderReadonlyFieldsBySettings<TagReadonlyFirstFirst>(readonlyFirstComesFirst: true);
        }

        private static void ShouldOrderReadonlyFieldsBySettings<TTag>(bool readonlyFirstComesFirst)
        {
            var value = new ReadonlyOrderProbe<TTag> { A = "a" };
            var byName = Configure(forceReflection: false);
            var readonlyFirst = Configure(forceReflection: true);

            Write(readonlyFirstComesFirst ? readonlyFirst : byName, value);

            Convert.ToHexString(Write(byName, value)).Should().Be(ByNameBytes);
            Convert.ToHexString(Write(readonlyFirst, value)).Should().Be(ReadonlyFirstBytes);
            WriteThenRead(byName, value).Should().BeEquivalentTo(value);
            WriteThenRead(readonlyFirst, value).Should().BeEquivalentTo(value);
        }

        private static Settings Configure(bool forceReflection)
        {
            return new Settings { UseSerializedVersionId = false, ForceReflectionToSetReadonlyFields = forceReflection }.MarkSerializable(_ => true);
        }

        private static byte[] Write<T>(Settings settings, T value)
        {
            using var binary = Binary.Create(settings);
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            return stream.ToArray();
        }

        private static T WriteThenRead<T>(Settings settings, T value)
        {
            using var binary = Binary.Create(settings);
            using var stream = new MemoryStream();
            binary.Write(value, stream);
            stream.Position = 0;
            return binary.Read<T>(stream);
        }
    }
}
