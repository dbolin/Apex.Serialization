using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class InheritanceTests : AbstractSerializerTestBase
    {
        public class Base
        {
            public Base(int v)
            {
                BaseValue = v;
            }
            private int BaseValue;

            public int BaseValueProp => BaseValue;
        }

        public class Derived : Base
        {
            public Derived(int v) : base(v)
            { }
            public int DerivedValue;
        }

        [Fact]
        public void InheritedFields()
        {
            var x = new Derived(1) {DerivedValue = 3};

            RoundTrip(x);
        }
    }
}
