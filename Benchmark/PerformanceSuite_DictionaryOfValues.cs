using BenchmarkDotNet.Attributes;
using System.Collections.Generic;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_DictionaryOfValues : PerformanceSuiteBase
    {
        private Dictionary<int, int> _d = new Dictionary<int, int>();

        public PerformanceSuite_DictionaryOfValues()
        {
            for(int i=0;i<100;++i)
            {
                _d.Add(i, i);
            }

            S_DictionaryOfValues();
        }

        [Benchmark]
        public void S_DictionaryOfValues()
        {
            Serialize(_d);
        }

        [Benchmark]
        public object D_DictionaryOfValues()
        {
            return Deserialize<Dictionary<int, int>>();
        }
    }
}
