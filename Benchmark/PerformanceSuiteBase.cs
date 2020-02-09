using Apex.Serialization;
using Ceras;
using System.IO;

#nullable disable

namespace Benchmark
{
    public class PerformanceSuiteBase
    {
        private readonly IBinary binary = Binary.Create(new Settings().MarkSerializable(x => true));
        private readonly IBinary binaryGraph = Binary.Create(new Settings { SerializationMode = Mode.Graph}.MarkSerializable(x => true));
        private readonly IBinary binaryWithVersionIds = Binary.Create(new Settings { UseSerializedVersionId = true }.MarkSerializable(x => true));
        private readonly MemoryStream m1 = new MemoryStream();
        private readonly MemoryStream m2 = new MemoryStream();
        private readonly MemoryStream m3 = new MemoryStream();

        private readonly CerasSerializer ceras = new CerasSerializer(new SerializerConfig { DefaultTargets = TargetMember.AllFields, PreserveReferences = false });
        private byte[] b = new byte[16];

        protected void Serialize<T>(T obj)
        {
            m1.Position = 0;
            binary.Write(obj, m1);

            //ceras.Serialize(obj, ref b);
        }

        protected T Deserialize<T>()
        {
            m1.Position = 0;
            return binary.Read<T>(m1);

            //return ceras.Deserialize<T>(b);
        }

        protected void SerializeGraph<T>(T obj)
        {
            m2.Position = 0;
            binaryGraph.Write(obj, m2);

            //ceras.Serialize(obj, ref b);
        }

        protected T DeserializeGraph<T>()
        {
            m2.Position = 0;
            return binaryGraph.Read<T>(m2);

            //return ceras.Deserialize<T>(b);
        }

        protected void SerializeWithVersionIds<T>(T obj)
        {
            m3.Position = 0;
            binaryWithVersionIds.Write(obj, m3);

            //ceras.Serialize(obj, ref b);
        }

        protected T DeserializeWithVersionIds<T>()
        {
            m3.Position = 0;
            return binaryWithVersionIds.Read<T>(m3);

            //return ceras.Deserialize<T>(b);
        }
    }
}
