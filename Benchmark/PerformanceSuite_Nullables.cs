using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_Nullables : PerformanceSuiteBase
    {
        [StructLayout(LayoutKind.Explicit)]
        public struct Struct1
        {
            [FieldOffset(0)]
            public decimal DecimalValue;
            [FieldOffset(0)]
            public Guid GuidValue;
        }

        public class Wrapper
        {
            public Struct1? NullableField;
        }

        private readonly List<Wrapper> _emptyListFull = new List<Wrapper>(Enumerable.Range(0, 1024).Select(x => new Wrapper()));
        public PerformanceSuite_Nullables()
        {
            _emptyListFull.Capacity = 1024;
            S_Nullables();
        }

        [Benchmark]
        public void S_Nullables()
        {
            Serialize(_emptyListFull);
        }

        [Benchmark]
        public object D_Nullables()
        {
            return Deserialize<List<Wrapper>>();
        }
    }
}
