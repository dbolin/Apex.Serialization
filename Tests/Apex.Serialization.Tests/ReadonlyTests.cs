using Apex.Serialization.Internal.Reflection;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class ReadonlyTests : AbstractSerializerTestBase
    {
        public struct TestStruct
        {
            public int Value1;
            public int Value2;
        }

        public class Test1
        {
            public readonly int PublicField;
            private readonly int privateField;
            public int PublicProperty { get; }
            private int privateProperty { get; }

            private readonly TestStruct privateStruct;

            public Test1(int a, int b, int c, int d, TestStruct test)
            {
                PublicField = a;
                privateField = b;
                PublicProperty = c;
                privateProperty = d;
                privateStruct = test;
            }

            public (int, int, int, int, TestStruct) GetValues()
            {
                return (PublicField, privateField, PublicProperty, privateProperty, privateStruct);
            }
        }

        public struct Test2
        {
            public Test2(int v, int a, int z)
            {
                A = a;
                Value = v;
                Z = z;
            }

            public int A;
            public readonly int Value;
            public int Z;
        }

        [Fact]
        public void PrivateFieldsAndProperties()
        {
            var x = new Test1(1, 2, 3, 4, new TestStruct { Value1 = 6, Value2 = 7 });

            RoundTrip(x, (a, b) => a.GetValues().Should().BeEquivalentTo(b.GetValues()));
        }

        [Fact]
        public void BoxedStruct()
        {
            var t = FieldInfoModifier.setFieldInfoNotReadonly;
            FieldInfoModifier.setFieldInfoNotReadonly = null;
            var x = (object) new Test2(5, 1, 3);

            RoundTrip(x);
            FieldInfoModifier.setFieldInfoNotReadonly = t;
        }

        [Fact]
        public void Struct()
        {
            var t = FieldInfoModifier.setFieldInfoNotReadonly;
            FieldInfoModifier.setFieldInfoNotReadonly = null;
            var x = new Test2(5, 1, 3);

            RoundTrip(x);
            FieldInfoModifier.setFieldInfoNotReadonly = t;
        }

#if NETCOREAPP
        [Fact]
        public void ShouldBeAbleToSetReadonlyFieldsDirectly()
        {
            FieldInfoModifier.MustUseReflectionToSetReadonly.Should().BeFalse();
        }
#endif
    }
}
