using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Collections.Immutable;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_ISD_IS_FullHistory : PerformanceSuiteBase
    {
        private List<ImmutableSortedDictionary<int, string>> _isd_is_list = new List<ImmutableSortedDictionary<int, string>>();

        public PerformanceSuite_ISD_IS_FullHistory()
        {
            var x = ImmutableSortedDictionary<int, string>.Empty;
            for(int i=0;i<100;++i)
            {
                x = x.Add(i, i.ToString());
                _isd_is_list.Add(x);
            }

            S_ISD_IS_FullHistory();
        }

        [Benchmark]
        public void S_ISD_IS_FullHistory()
        {
            Serialize(_isd_is_list);
        }

        [Benchmark]
        public object D_ISD_IS_FullHistory()
        {
            return Deserialize<List<ImmutableSortedDictionary<int, string>>>();
        }
    }
}
