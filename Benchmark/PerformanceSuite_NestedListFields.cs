using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Linq;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_NestedListFields : PerformanceSuiteBase
    {
        public sealed class Level1F
        {
            public sealed class Level2F
            {
                public sealed class Level3F
                {
                    public string A;
                    public int? A2;
                    public int? B;
                    public int? C;
                    public int? D;
                    public decimal E;
                    public string F;
                }

                public string A;
                public int? A2;
                public int? B;
                public int? C;
                public int? D;
                public decimal E;
                public string F;
                public Level3F Ref;
            }

            public string A;
            public int? A2;
            public int? B;
            public int? C;
            public int? D;
            public decimal E;
            public string F;
            public Level2F Ref;
        }

        private readonly List<Level1F> _nestedListFields = Enumerable.Range(0, 333).Select(x => new Level1F { Ref = new Level1F.Level2F { Ref = new Level1F.Level2F.Level3F() } }).ToList();

        public PerformanceSuite_NestedListFields()
        {
            S_NestedListFields();
        }

        [Benchmark]
        public void S_NestedListFields()
        {
            Serialize(_nestedListFields);
        }

        [Benchmark]
        public object D_NestedListFields()
        {
            return Deserialize<List<Level1F>>();
        }
    }
}
