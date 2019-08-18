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

            STree_ISD_IS_FullHistory();
            SGraph_ISD_IS_FullHistory();
        }

        [Benchmark]
        public void STree_ISD_IS_FullHistory()
        {
            Serialize(_isd_is_list);
        }

        [Benchmark]
        public object DTree_ISD_IS_FullHistory()
        {
            return Deserialize<List<ImmutableSortedDictionary<int, string>>>();
        }

        [Benchmark]
        public void SGraph_ISD_IS_FullHistory()
        {
            SerializeGraph(_isd_is_list);
        }

        [Benchmark]
        public object DGraph_ISD_IS_FullHistory()
        {
            return DeserializeGraph<List<ImmutableSortedDictionary<int, string>>>();
        }
    }
}
