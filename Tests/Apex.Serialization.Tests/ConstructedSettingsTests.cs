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
            var settings = new Settings { InliningMaxDepth = 0, SupportSerializationHooks = true }.MarkSerializable(typeof(List<>));
            var imm1 = settings.ToImmutable();
            settings = new Settings { InliningMaxDepth = 0, SupportSerializationHooks = true }.MarkSerializable(typeof(List<>));
            var imm2 = settings.ToImmutable();
            imm1.GetHashCode().Should().Be(imm2.GetHashCode());
            imm1.Equals(imm2).Should().BeTrue();
        }

        public class Test
        {
            public int Value;

            public static void Serialize(Test t, IBinaryWriter writer)
            {
                writer.Write(t.Value - 1);
            }

            public static Test Deserialize(IBinaryReader reader)
            {
                return new Test { Value = reader.Read<int>() };
            }
        }

        [Fact]
        public void CustomSerializationActions()
        {
            var settings1 = new Settings().RegisterCustomSerializer<Test>(Test.Serialize, Test.Deserialize);
            var imm1 = settings1.ToImmutable();
            var settings2 = new Settings().RegisterCustomSerializer<Test>(Test.Serialize, Test.Deserialize);
            var imm2 = settings2.ToImmutable();

            imm1.GetHashCode().Should().Be(imm2.GetHashCode());
            imm1.Equals(imm2).Should().BeTrue();
        }

        [Fact]
        public void CustomSerializationActionsLambdaNoClosure()
        {
            var imm1 = TestLambda();
            var imm2 = TestLambda();

            imm1.GetHashCode().Should().Be(imm2.GetHashCode());
            imm1.Equals(imm2).Should().BeTrue();
        }

        private ImmutableSettings TestLambda()
        {
            var settings = new Settings().RegisterCustomSerializer<Test>((t,w) => w.Write(t.Value), Test.Deserialize);
            return settings.ToImmutable();
        }

        [Fact]
        public void CustomSerializationActionsLambdaWithClosure()
        {
            var imm1 = TestLambdaWithClosure(1);
            var imm2 = TestLambdaWithClosure(2);

            imm1.GetHashCode().Should().NotBe(imm2.GetHashCode());
            imm1.Equals(imm2).Should().BeFalse();
        }

        private ImmutableSettings TestLambdaWithClosure(int a)
        {
            var settings = new Settings().RegisterCustomSerializer<Test>((t, w) => w.Write(t.Value + a), Test.Deserialize);
            return settings.ToImmutable();
        }
    }
}
