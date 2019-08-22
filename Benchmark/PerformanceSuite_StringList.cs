using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Linq;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_StringList : PerformanceSuiteBase
    {
        private readonly List<string> _list = new List<string>(Enumerable.Range(0, 1024).Select(x => "asd"));
        public PerformanceSuite_StringList()
        {
            _list.Capacity = 1024;
            S_StringList();
        }

        [Benchmark]
        public void S_StringList()
        {
            Serialize(_list);
        }

        [Benchmark]
        public object D_StringList()
        {
            return Deserialize<List<string>>();
        }
    }
}
