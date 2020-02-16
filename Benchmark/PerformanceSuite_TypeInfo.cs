using BenchmarkDotNet.Attributes;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_TypeInfo : PerformanceSuiteBase
    {
        public class Empty
        { }

        private readonly object _empty = new Empty();

        public PerformanceSuite_TypeInfo()
        {
            S_SingleEmptyObject();
        }

        [Benchmark]
        public void S_SingleEmptyObject()
        {
            Serialize(_empty);
        }

        [Benchmark]
        public object D_SingleEmptyObject()
        {
            return Deserialize<object>();
        }
    }
}
