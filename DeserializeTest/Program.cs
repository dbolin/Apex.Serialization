using Apex.Serialization;
using System;
using System.IO;

namespace DeserializeTest
{
    public abstract class TestO
    {
        public int O;
    }

    public class TestA : TestO
    {
        public int A;
    }

    public class TestB : TestA { }

    public class Test1 : TestA
    {
        public static IBinary CreateSerializer()
        {
            return Binary.Create(new Settings { UseSerializedVersionId = true, FlattenClassHierarchy = false }.MarkSerializable(typeof(Test1)).MarkSerializable(typeof(Test2<>)));
        }

        public sealed class Test2<T>
        {
            public T V;
        }

        public int A;
        public decimal? B;
        public string C;
        public Test2<int> D;
        public TestO E;
    }

    class Program
    {
        static int Main(string[] args)
        {
            var data = args.Length > 0 ? Convert.FromBase64String(args[0]) : null;
            try
            {
                if (data != null)
                {
                    using var fs = new MemoryStream(data);
                    using var s = Test1.CreateSerializer();
                    var x = s.Read<Test1>(fs);
                    if (x.A == 0)
                    {
                        return 2;
                    }
                }
                else
                {
                    var y = new Test1 { A = 1, B = 12, C = new string('a', 1111), D = new Test1.Test2<int> { V = 1 } };
                    using var fs = new MemoryStream();
                    using var s = Test1.CreateSerializer();
                    s.Write(y, fs);
                    Console.WriteLine(Convert.ToBase64String(fs.ToArray()));
                }
            }
            catch(Exception e)
            {
                Console.WriteLine(e.ToString());
                return 1;
            }

            return 0;
        }
    }
}
