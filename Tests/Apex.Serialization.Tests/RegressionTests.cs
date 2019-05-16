using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class RegressionTests : AbstractSerializerTestBase
    {
        [Fact]
        public void Test1()
        {
            var t = new List<TestObject1>();

            for(int i=0;i<100;++i)
            {
                t.Add(GenerateObject(i));
            }

            var arr = t.ToArray();

            var x = RoundTrip(arr, (a,b) => true);
        }

        private TestObject1 GenerateObject(int i)
        {
            var properties = ImmutableArray<CustomProperty>.Empty.ToBuilder();

            for(int j=0;j<i;++j)
            {
                properties.Add(new CustomProperty(j.ToString(), new Value() { _string = "a" }));
            }

            return new TestObject1(new Value() { _string = "a" }, "", false, false, 123, null, null, "", properties.ToImmutable());
        }
    }


    public sealed class TestObject1
    {
        public Value Value { get; }
        public string StringValue { get; }
        public string Description { get; }
        public bool IsVisible { get; }
        public bool IsLocked { get; }
        public decimal Order { get; }
        public string ImageLink { get; }
        public string Tooltip { get; }
        public string InfoMessage { get; }
        public ImmutableArray<CustomProperty> CustomProperties { get; }

        public TestObject1(Value value, string description, bool isVisible, bool isLocked, decimal order,
            string imageLink, string tooltip, string infoMessage,
            ImmutableArray<CustomProperty> customProperties)
        {
            Value = value;
            StringValue = value.ToString();
            Description = description ?? StringValue;
            IsVisible = isVisible;
            IsLocked = isLocked;
            Order = order;
            ImageLink = imageLink;
            Tooltip = tooltip;
            InfoMessage = infoMessage;
            CustomProperties = customProperties;
        }
    }

    public struct CustomProperty
    {
        public CustomProperty(string key, Value value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }
        public Value Value { get; }
    }

    public sealed class Value
    {
        internal PrimitiveValue? _primitive;
        internal string _string;
        internal object _collection;
        internal object _array;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PrimitiveValue
    {
        [FieldOffset(0)] public decimal Number;
        [FieldOffset(0)] public bool Boolean;
        [FieldOffset(0)] public DateTime DateTime;
        [FieldOffset(0)] public Guid Guid;
    }
}
