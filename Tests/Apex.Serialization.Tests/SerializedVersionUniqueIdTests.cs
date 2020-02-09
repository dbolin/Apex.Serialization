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
        public void TestConsistentId()
        {
            var m = new MemoryStream();
            var v = new CustomProperty("a", new Value { _primitive = new PrimitiveValue { Number = 2 } });
            var settings = new Settings { DisableInlining = true }.MarkSerializable(x => true);

            using var sut0 = Binary.Create(settings);
            sut0.Write(v, m);
            DynamicCodeMethods._virtualWriteMethods.Clear();

            using var sut1 = Binary.Create(settings);
            sut1.Write(v, m);
            var ids = DynamicCodeMethods._virtualWriteMethods.ToDictionary(x => x.Key, x => x.Value.SerializedVersionUniqueId);

            DynamicCodeMethods._virtualWriteMethods.Clear();
            using var sut2 = Binary.Create(settings);
            sut2.Write(v, m);
            var ids2 = DynamicCodeMethods._virtualWriteMethods.ToDictionary(x => x.Key, x => x.Value.SerializedVersionUniqueId);

            ids2.Count.Should().Be(ids.Count);

            foreach(var kvp in ids)
            {
                ids2[kvp.Key].Should().Be(kvp.Value);
            }
        }

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
    }
}
