using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Linq;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_NestedListEmpty : PerformanceSuiteBase
    {
        public sealed class Level1
        {
            public sealed class Level2
            {
                public sealed class Level3
                {
                }

                public Level3 Ref;
            }

            public Level2 Ref;
        }

        private readonly List<Level1> _nestedList = Enumerable.Range(0, 333).Select(x => new Level1 { Ref = new Level1.Level2 { Ref = new Level1.Level2.Level3() } }).ToList();

        public PerformanceSuite_NestedListEmpty()
        {
            S_NestedListEmpty();
        }

        [Benchmark]
        public void S_NestedListEmpty()
        {
            Serialize(_nestedList);
        }

        [Benchmark]
        public object D_NestedListEmpty()
        {
            return Deserialize<List<Level1>>();
        }
    }
}
