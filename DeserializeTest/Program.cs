using Apex.Serialization;
using System;
using System.IO;

namespace DeserializeTest
{
    public sealed class Test1
    {
        public static IBinary CreateSerializer()
        {
            return Binary.Create(new Settings { UseSerializedVersionId = true }.MarkSerializable(typeof(Test1)).MarkSerializable(typeof(Test2<>)));
        }

        public sealed class Test2<T>
        {
            public T V;
        }

        public int A;
        public decimal? B;
        public string C;
        public Test2<int> D;
    }

    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var file = args[0];
                if (File.Exists(file))
                {
                    using var fs = new FileStream(file, FileMode.Open);
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
                    using var fs = new FileStream(file, FileMode.Create);
                    using var s = Test1.CreateSerializer();
                    s.Write(y, fs);
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
