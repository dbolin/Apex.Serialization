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

        public class StaticTest
        {
            public int InnerValue;

            [NonSerialized]
            public int CachedValue;

            [AfterDeserialization]
            private static void AfterDeserializationMethod(StaticTest o)
            {
                o.CachedValue = o.InnerValue;
            }
        }

        public class StaticTestContext
        {
            public int Value;
        }

        public class StaticTestWithContext
        {
            public int InnerValue;

            [NonSerialized]
            public int CachedValue;

            [AfterDeserialization]
            private static void AfterDeserializationMethod(StaticTestWithContext o, StaticTestContext context)
            {
                o.CachedValue = (context?.Value) ?? 0;
            }
        }

        public class MethodTestContext
        {
            public int Value;
        }

        public class MethodTestWithContext
        {
            public int InnerValue;

            [NonSerialized]
            public int CachedValue;

            [AfterDeserialization]
            private void AfterDeserializationMethod(MethodTestContext context)
            {
                CachedValue = (context?.Value) ?? 0;
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

        [Fact]
        public void StaticMethod()
        {
            var x = new StaticTest { InnerValue = 3, CachedValue = 3 };

            RoundTrip(x);
        }

        [Fact]
        public void StaticMethodWithContext()
        {
            var context = new StaticTestContext { Value = 3 };
            var x = new StaticTestWithContext { InnerValue = 3, CachedValue = 3 };

            (_serializer as IBinary).SetCustomHookContext(context);
            (_serializerGraph as IBinary).SetCustomHookContext(context);

            RoundTrip(x);
        }

        [Fact]
        public void NullContext()
        {
            var x = new StaticTestWithContext { InnerValue = 3, CachedValue = 0 };

            RoundTrip(x);
        }

        [Fact]
        public void MethodWithContext()
        {
            var context = new MethodTestContext { Value = 3 };
            var x = new MethodTestWithContext { InnerValue = 3, CachedValue = 3 };

            (_serializer as IBinary).SetCustomHookContext(context);
            (_serializerGraph as IBinary).SetCustomHookContext(context);

            RoundTrip(x);
        }

    }
}
