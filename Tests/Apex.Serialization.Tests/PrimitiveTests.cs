using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Apex.Serialization.Tests
{
    public class PrimitiveTests : AbstractSerializerTestBase
    {
        public class Test
        {
            public byte a;
            public sbyte b;
            public short c;
            public ushort d;
            public char e;
            public int f;
            public uint g;
            public long h;
            public ulong i;
            public bool j;
            public float k;
            public double l;
            public decimal m;
            public Guid n;
        }

        public class Test2
        {
            public object? a;
            public object? b;
            public object? c;
            public object? d;
            public object? e;
            public object? f;
            public object? g;
            public object? h;
            public object? i;
            public object? j;
            public object? k;
            public object? l;
            public object? m;
            public object? n;
        }

        [Fact]
        public void Primitives()
        {
            var x = new Test
            {
                a = byte.MaxValue,
                b = sbyte.MaxValue,
                c = short.MaxValue,
                d = ushort.MaxValue,
                e = char.MaxValue,
                f = int.MaxValue,
                g = uint.MaxValue,
                h = long.MaxValue,
                i = ulong.MaxValue,
                j = true,
                k = float.Epsilon,
                l = double.Epsilon,
                m = decimal.MaxValue,
                n = Guid.Parse("12341234-1234-1234-1234-123412341234")
            };

            RoundTrip(x);
        }

        [Fact]
        public void BoxedPrimitives()
        {
            var x = new Test2
            {
                a = byte.MaxValue,
                b = sbyte.MaxValue,
                c = short.MaxValue,
                d = ushort.MaxValue,
                e = char.MaxValue,
                f = int.MaxValue,
                g = uint.MaxValue,
                h = long.MaxValue,
                i = ulong.MaxValue,
                j = true,
                k = float.Epsilon,
                l = double.Epsilon,
                m = decimal.MaxValue,
                n = Guid.Parse("12341234-1234-1234-1234-123412341234")
            };

            RoundTrip(x);
        }

        [Fact]
        public void DateTimeTest()
        {
            var x = DateTime.UtcNow.ToLocalTime();

            var y = RoundTrip(x);

            y.Kind.Should().Be(x.Kind);
        }
    }
}
