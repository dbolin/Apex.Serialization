using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Linq;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_NullableInts : PerformanceSuiteBase
    {
        private readonly List<int?> _listInt = new List<int?>(Enumerable.Range(0, 1024).Select(x => (int?)x));

        public PerformanceSuite_NullableInts()
        {
            _listInt.Capacity = 1024;
            S_NullableInts();
        }

        [Benchmark]
        public void S_NullableInts()
        {
            Serialize(_listInt);
        }

        [Benchmark]
        public object D_NullableInts()
        {
            return Deserialize<List<int?>>();
        }
    }
}
