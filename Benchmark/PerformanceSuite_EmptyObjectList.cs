using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Linq;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_EmptyObjectList : PerformanceSuiteBase
    {
        public sealed class Empty
        { }

        private readonly List<Empty> _emptyListFull = new List<Empty>(Enumerable.Range(0, 1024).Select(x => new Empty()));
        public PerformanceSuite_EmptyObjectList()
        {
            _emptyListFull.Capacity = 1024;
            S_ListEmptyFull();
        }

        [Benchmark]
        public void S_ListEmptyFull()
        {
            Serialize(_emptyListFull);
        }

        [Benchmark]
        public object D_ListEmptyFull()
        {
            return Deserialize<List<Empty>>();
        }
    }
}
