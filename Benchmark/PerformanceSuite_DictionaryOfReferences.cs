using BenchmarkDotNet.Attributes;
using System.Collections.Generic;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_DictionaryOfReferences : PerformanceSuiteBase
    {
        private Dictionary<object, object> _d = new Dictionary<object, object>();

        public PerformanceSuite_DictionaryOfReferences()
        {
            for(int i=0;i<100; ++i)
            {
                _d.Add(new object(), new object());
            }

            S_DictionaryOfReferences();
        }

        [Benchmark]
        public void S_DictionaryOfReferences()
        {
            Serialize(_d);
        }

        [Benchmark]
        public object D_DictionaryOfReferences()
        {
            return Deserialize<Dictionary<object, object>>();
        }
    }
}
