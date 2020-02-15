using System.IO;
using Apex.Serialization;
using BenchmarkDotNet.Attributes;
using Hyperion;
using MessagePack;
using ProtoBuf;
using ZeroFormatter;
using Serializer = Hyperion.Serializer;

namespace Benchmark
{
    public class SerializationArray
    {
        [ProtoContract]
        [MessagePackObject]
        public class Wrapper
        {
            [ProtoMember(1)]
            [Key(0)]
            public int[] _t1 = new int[50000];
        }

        private Wrapper _t1 = new Wrapper();

        private readonly MemoryStream _memoryStream = new MemoryStream();

        private readonly IBinary _binaryTree = Binary.Create(new Settings
        { SerializationMode = Mode.Tree, UseSerializedVersionId = false }.MarkSerializable(x => true));

        //private readonly Serializer _hyperion = new Serializer(new SerializerOptions());

        public SerializationArray()
        {
            Protobuf();
            MessagePack();
            //Hyperion();
            //ZeroFormatter();
            Apex();
        }

        [Benchmark]
        public void Protobuf()
        {
            _memoryStream.Seek(0, SeekOrigin.Begin);
            ProtoBuf.Serializer.Serialize(_memoryStream, _t1);
        }

        [Benchmark]
        public void MessagePack()
        {
            _memoryStream.Seek(0, SeekOrigin.Begin);
            MessagePackSerializer.Serialize(_memoryStream, _t1);
        }
        /*
        [Benchmark]
        public void Hyperion()
        {
            _memoryStream.Seek(0, SeekOrigin.Begin);
            _hyperion.Serialize(_t1, _memoryStream);
        }
        */
        /*
        [Benchmark]
        public void ZeroFormatter()
        {
            _memoryStream.Seek(0, SeekOrigin.Begin);
            ZeroFormatterSerializer.Serialize(_memoryStream, _t1);
        }
        */

        [Benchmark(Baseline = true)]
        public void Apex()
        {
            _memoryStream.Seek(0, SeekOrigin.Begin);
            _binaryTree.Write(_t1, _memoryStream);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _binaryTree.Dispose();
        }

    }
}
