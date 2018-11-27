using Apex.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class AfterDeserializationTests : AbstractSerializerTestBase
    {
        public class Test
        {
            public int InnerValue;

            [NonSerialized]
            public int CachedValue;

            [AfterDeserialization]
            private void AfterDeserializationMethod()
            {
                CachedValue = InnerValue;
            }
        }

        public class Test2 : Test
        {
            public int Value;

            [NonSerialized]
            public int TestValue;

            [AfterDeserialization]
            private void TestingOrder()
            {
                TestValue = CachedValue;
            }
        }

        [Fact]
        public void MethodIsCalled()
        {
            var x = new Test {InnerValue = 3, CachedValue = 3};

            RoundTrip(x);
        }

        [Fact]
        public void MethodsRunInOrder()
        {
            var x = new Test2 {InnerValue = 3, CachedValue = 3, Value = 5, TestValue = 3};

            RoundTrip(x);
        }
    }
}
