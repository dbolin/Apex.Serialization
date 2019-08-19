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

            for (int i = 0; i < 100; ++i)
            {
                t.Add(GenerateObject(i));
            }

            var arr = t.ToArray();

            var x = RoundTrip(arr, (a, b) => true);
        }

        private TestObject1 GenerateObject(int i)
        {
            var properties = ImmutableArray<CustomProperty>.Empty.ToBuilder();

            for (int j = 0; j < i; ++j)
            {
                properties.Add(new CustomProperty(j.ToString(), new Value() { _string = "a" }));
            }

            return new TestObject1(new Value() { _string = "a" }, "", false, false, 123, null, null, "", properties.ToImmutable());
        }

        [Fact]
        public void Test2()
        {
            var o = new TestObject2 { A = new PrimitiveValue { Number = 1 }, B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid(), E = Guid.NewGuid() };
            var x = new[] { o, o, o };

            RoundTrip(x);
        }
    }

    public sealed class TestObject1
    {
        public Value? V { get; }
        public string? S { get; }
        public string? D { get; }
        public bool IsV { get; }
        public bool IsL { get; }
        public decimal O { get; }
        public string? Im { get; }
        public string? T { get; }
        public string? In { get; }
        public ImmutableArray<CustomProperty>? Cu { get; }

        public TestObject1(Value? v, string? d, bool isv, bool isl, decimal o,
            string? il, string? t, string? im,
            ImmutableArray<CustomProperty>? cu)
        {
            V = v;
            S = v.ToString();
            D = d ?? S;
            IsV = isv;
            IsL = isl;
            O = o;
            Im = il;
            T = t;
            In = im;
            Cu = cu;
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
        internal string? _string;
        internal object? _collection;
        internal object? _array;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PrimitiveValue
    {
        [FieldOffset(0)] public decimal Number;
        [FieldOffset(0)] public bool Boolean;
        [FieldOffset(0)] public DateTime DateTime;
        [FieldOffset(0)] public Guid Guid;
    }

    public sealed class TestObject2
    {
        public PrimitiveValue? A;
        public Guid B;
        public Guid C;
        public Guid D;
        public Guid E;
    }
}
