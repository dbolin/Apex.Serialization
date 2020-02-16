using Apex.Serialization.Extensions;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class ConstructedSettingsTests
    {
        [Fact]
        public void Duplicates()
        {
            int initialCount = Settings._constructedSettings.Count;
            var settings = new Settings { InliningMaxDepth = 0, SupportSerializationHooks = true }.MarkSerializable(typeof(List<>));
            _ = settings.ToImmutable();
            Settings._constructedSettings.Count.Should().Be(initialCount + 1);
            settings = new Settings { InliningMaxDepth = 0, SupportSerializationHooks = true }.MarkSerializable(typeof(List<>));
            _ = settings.ToImmutable();
            Settings._constructedSettings.Count.Should().Be(initialCount + 1);
        }

        public class Test
        {
            public int Value;

            public static void Serialize(Test t, IBinaryWriter writer)
            {
                writer.Write(t.Value - 1);
            }

            public static void Deserialize(Test t, IBinaryReader reader)
            {
                t.Value = reader.Read<int>();
            }
        }

        [Fact]
        public void CustomSerializationActions()
        {
            var settings1 = new Settings().RegisterCustomSerializer<Test>(Test.Serialize, Test.Deserialize);
            using var b1 = Binary.Create(settings1);
            var count = Settings._constructedSettings.Count;
            var settings2 = new Settings().RegisterCustomSerializer<Test>(Test.Serialize, Test.Deserialize);
            using var b2 = Binary.Create(settings1);
            var count2 = Settings._constructedSettings.Count;

            count2.Should().Be(count);
        }

        [Fact]
        public void CustomSerializationActionsLambdaNoClosure()
        {
            TestLambda();
            var count = Settings._constructedSettings.Count;
            TestLambda();
            var count2 = Settings._constructedSettings.Count;

            count2.Should().Be(count);
        }

        private void TestLambda()
        {
            var settings = new Settings().RegisterCustomSerializer<Test>((t,w) => w.Write(t.Value), Test.Deserialize);
            using var b = Binary.Create(settings);
        }

        [Fact]
        public void CustomSerializationActionsLambdaWithClosure()
        {
            TestLambdaWithClosure(1);
            var count = Settings._constructedSettings.Count;
            TestLambdaWithClosure(2);
            var count2 = Settings._constructedSettings.Count;

            count2.Should().Be(count + 1);
        }

        private void TestLambdaWithClosure(int a)
        {
            var settings = new Settings().RegisterCustomSerializer<Test>((t, w) => w.Write(t.Value + a), Test.Deserialize);
            using var b = Binary.Create(settings);
        }
    }
}
