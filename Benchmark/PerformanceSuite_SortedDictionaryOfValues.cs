using BenchmarkDotNet.Attributes;
using System.Collections.Generic;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_SortedDictionaryOfValues : PerformanceSuiteBase
    {
        private SortedDictionary<int, int> _d = new SortedDictionary<int, int>();

        public PerformanceSuite_SortedDictionaryOfValues()
        {
            for(int i=0;i<100;++i)
            {
                _d.Add(i, i);
            }

            S_SortedDictionaryOfValues();
        }

        [Benchmark]
        public void S_SortedDictionaryOfValues()
        {
            Serialize(_d);
        }

        [Benchmark]
        public object D_SortedDictionaryOfValues()
        {
            return Deserialize<SortedDictionary<int, int>>();
        }
    }
}
