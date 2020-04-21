using BenchmarkDotNet.Attributes;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuite_Inheritance : PerformanceSuiteBase
    {
        public class Test4
        {
            public int d = 4;
        }
        public class Test3 : Test4
        {
            public int b = 2;
            public string c = null;
        }
        public class Test2 : Test3 { }

        public sealed class Test1 : Test2
        {
            public int a = 1;
        }

        private readonly Test1 _test = new Test1();

        public PerformanceSuite_Inheritance()
        {
            S_Inheritance();
            S_InheritanceNotFlattened();
        }

        [Benchmark]
        public void S_Inheritance()
        {
            Serialize(_test);
        }

        [Benchmark]
        public object D_Inheritance()
        {
            return Deserialize<Test1>();
        }

        [Benchmark]
        public void S_InheritanceNotFlattened()
        {
            SerializeWithoutFlattenClassHierarchy(_test);
        }

        [Benchmark]
        public object D_InheritanceNotFlattened()
        {
            return DeserializeWithoutFlattenClassHierarchy<Test1>();
        }
    }
}
