using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_NullableWrappedStruct : PerformanceSuiteBase
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

        private readonly List<Wrapper> _listWrapper = new List<Wrapper>(Enumerable.Range(0, 1024).Select(x => new Wrapper()));

        public PerformanceSuite_NullableWrappedStruct()
        {
            _listWrapper.Capacity = 1024;
            S_NullableWrapper();
        }

        [Benchmark]
        public void S_NullableWrapper()
        {
            Serialize(_listWrapper);
        }

        [Benchmark]
        public object D_NullableWrapper()
        {
            return Deserialize<List<Wrapper>>();
        }
    }
}
