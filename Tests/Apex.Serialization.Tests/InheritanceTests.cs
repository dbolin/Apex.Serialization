using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class InheritanceTests : AbstractSerializerTestBase
    {
        public static Type[] SerializableTypes()
        {
            return new[]
            {
                typeof(List<>)
            };
        }

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

        public class DerivedFromList : List<int>
        {
            public int Value;
        }

        [Fact]
        public void InheritedFields()
        {
            var x = new Derived(1) {DerivedValue = 3};

            RoundTrip(x);
        }

        [Fact]
        public void InheritFromList()
        {
            var x = new DerivedFromList { Value = 5 };
            x.Add(2);
            x.Add(3);

            RoundTrip(x);

            var y = new List<int>();
            y.Add(2);
            y.Add(3);

            RoundTrip(y);
        }
    }
}
