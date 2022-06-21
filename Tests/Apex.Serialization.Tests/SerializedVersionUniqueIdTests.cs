using Apex.Serialization.Internal;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class SerializedVersionUniqueIdTests
    {
        [Fact]
        public void MismatchingIds()
        {
            var m = new MemoryStream();
            var v = new CustomProperty("a", new Value { _primitive = new PrimitiveValue { Number = 2 } });
            var settings = new Settings { UseSerializedVersionId = true }.MarkSerializable(x => true);

            using var sut = Binary.Create(settings);
            sut.Write(v, m);

            m.Position = 0;
            Assert.Throws<InvalidOperationException>(() => sut.Read<KeyValuePair<int, int>>(m));
        }

        public static readonly IEnumerable<object[]> HardCodedTypeIds = new[] {
            new object[] {typeof(int), 494594721},
            new object[] {typeof(PrimitiveValue), 1938464263},
            new object[] {typeof(Value), 1445229640},
            new object[] {typeof(CustomProperty), 797909415},
        };

        [Theory]
        [MemberData(nameof(HardCodedTypeIds))]
        public void TestHardCoded(Type type, int expectedValue)
        {
            var settings = new Settings { InliningMaxDepth = -1 }.MarkSerializable(x => true);

            using var sut0 = Binary.Create(settings);
            sut0.Precompile(type);

            var id = DynamicCodeMethods._virtualWriteMethods.Single(x => x.Key.Type == type && x.Key.IncludesTypeInfo == true && x.Key.Settings.InliningMaxDepth == -1).Value.SerializedVersionUniqueId;

            id.Should().Be(expectedValue);
        }
    }
}
