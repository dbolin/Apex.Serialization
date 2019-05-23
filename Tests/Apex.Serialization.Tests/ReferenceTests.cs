using System.IO;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class ReferenceTests : AbstractSerializerTestBase
    {
        public sealed class TestB
        {
            internal int Value = 1;
        }

        public class TestC
        {
            internal int Value = 2;
        }

        public class TestD : TestC
        {
            internal int Value = 3;
        }

        public sealed class TestObject
        {
            public int Value;
            public string Name;
            public TestB Ref;
        }

        public sealed class TestObject2
        {
            public int Value;
            public string Name;
            public TestC Ref;
        }

        public struct TestStruct1
        {
            public TestB Ref;
        }

        public struct TestStruct2
        {
            public TestC Ref;
        }

        public class TestE
        {
            public object Ref;
            public object Struct;
        }

        [Fact]
        public void SealedObjectWithNullReferenceToSealedType()
        {
            var x = new TestObject
            {
                Name = "A"
            };

            RoundTrip(x);
        }

        [Fact]
        public void SealedObjectWithNullReferenceToUnsealedType()
        {
            var x = new TestObject2
            {
                Name = "A"
            };

            RoundTrip(x);
        }

        [Fact]
        public void StructWithNullReferenceToSealedType()
        {
            var x = new TestStruct1();

            RoundTrip(x);
        }

        [Fact]
        public void StructWithNullReferenceToUnsealedType()
        {
            var x = new TestStruct2();

            RoundTrip(x);
        }

        [Fact]
        public void SealedObjectWithReferenceToSealedType()
        {
            var x = new TestObject
            {
                Name = "A",
                Ref = new TestB()
            };

            RoundTrip(x);
        }

        [Fact]
        public void SealedObjectWithReferenceToUnsealedType()
        {
            var x = new TestObject2
            {
                Name = "A",
                Ref = new TestC()
            };

            RoundTrip(x);
            RoundTrip(x);
        }

        [Fact]
        public void StructWithReferenceToSealedType()
        {
            var x = new TestStruct1
            {
                Ref = new TestB()
            };

            RoundTrip(x, (original, loaded) => original.Ref.Value == loaded.Ref.Value);
        }

        [Fact]
        public void StructWithReferenceToUnsealedType()
        {
            var x = new TestStruct2
            {
                Ref = new TestC()
            };

            RoundTrip(x, (original, loaded) => original.Ref.Value == loaded.Ref.Value);
        }

        [Fact]
        public void StructWithReferenceToDerivedType()
        {
            var x = new TestStruct2
            {
                Ref = new TestD { Value = 13 }
            };

            RoundTrip(x, (original, loaded) => original.Ref.Value == loaded.Ref.Value && ((TestD)loaded.Ref).Value == 13);
        }

        [Fact]
        public void ClassWithObjectFields()
        {
            var x = new TestE
            {
                Ref = new TestB { Value = 3},
                Struct = new TestStruct1
                {
                    Ref = new TestB { Value = 4}
                }
            };

            RoundTrip(x, (a, b) =>
            {
                a.Ref.Should().BeEquivalentTo(b.Ref);
                ((TestStruct1) a.Struct).Ref.Should().BeEquivalentTo(((TestStruct1) b.Struct).Ref);
            });
        }
    }
}
