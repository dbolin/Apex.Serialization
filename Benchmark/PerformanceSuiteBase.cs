using Apex.Serialization;
using Ceras;
using System.IO;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuiteBase
    {
        private readonly IBinary binary = Binary.Create(new Settings { UseConstructors = true });
        private readonly IBinary binaryGraph = Binary.Create(new Settings { SerializationMode = Mode.Graph, UseConstructors = true });
        private readonly MemoryStream m = new MemoryStream();

        private readonly CerasSerializer ceras = new CerasSerializer(new SerializerConfig { DefaultTargets = TargetMember.AllFields, PreserveReferences = false });
        private byte[] b = new byte[16];

        protected void Serialize<T>(T obj)
        {
            m.Position = 0;
            binary.Write(obj, m);

            //ceras.Serialize(obj, ref b);
        }

        protected T Deserialize<T>()
        {
            m.Position = 0;
            return binary.Read<T>(m);

            //return ceras.Deserialize<T>(b);
        }

        protected void SerializeGraph<T>(T obj)
        {
            m.Position = 0;
            binaryGraph.Write(obj, m);

            //ceras.Serialize(obj, ref b);
        }

        protected T DeserializeGraph<T>()
        {
            m.Position = 0;
            return binaryGraph.Read<T>(m);

            //return ceras.Deserialize<T>(b);
        }
    }
}
