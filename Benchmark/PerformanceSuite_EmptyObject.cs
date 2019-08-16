using BenchmarkDotNet.Attributes;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_EmptyObject : PerformanceSuiteBase
    {
        public sealed class Empty
        { }

        private readonly Empty _empty = new Empty();

        public PerformanceSuite_EmptyObject()
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
            return Deserialize<Empty>();
        }
    }
}
