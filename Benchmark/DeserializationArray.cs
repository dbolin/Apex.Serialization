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
    public class DeserializationArray
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

        private readonly MemoryStream _m1 = new MemoryStream();
        private readonly MemoryStream _m2 = new MemoryStream();
        private readonly MemoryStream _m3 = new MemoryStream();
        private readonly MemoryStream _m4 = new MemoryStream();
        private readonly MemoryStream _m5 = new MemoryStream();

        private readonly IBinary _binaryTree = Binary.Create(new Settings
        { SerializationMode = Mode.Tree });

        //private readonly Serializer _hyperion = new Serializer(new SerializerOptions());

        public DeserializationArray()
        {
            ProtoBuf.Serializer.Serialize(_m1, _t1);
            MessagePackSerializer.Serialize(_m2, _t1);
            //_hyperion.Serialize(_t1, _m3);
            //ZeroFormatterSerializer.Serialize(_m4, _t1);
            _binaryTree.Write(_t1, _m5);
        }

        [Benchmark]
        public object Protobuf()
        {
            _m1.Seek(0, SeekOrigin.Begin);
            return ProtoBuf.Serializer.Deserialize<Wrapper>(_m1)._t1[0];
        }

        [Benchmark]
        public object MessagePack()
        {
            _m2.Seek(0, SeekOrigin.Begin);
            return MessagePackSerializer.Deserialize<Wrapper>(_m2)._t1[0];
        }

        /*
        [Benchmark]
        public object Hyperion()
        {
            _m3.Seek(0, SeekOrigin.Begin);
            return _hyperion.Deserialize<Wrapper>(_m3)._t1[0];
        }

        [Benchmark]
        public object ZeroFormatter()
        {
            _m4.Seek(0, SeekOrigin.Begin);
            return ZeroFormatterSerializer.Deserialize<Wrapper>(_m4)._t1[0];
        }
        */

        [Benchmark(Baseline = true)]
        public object Apex()
        {
            _m5.Seek(0, SeekOrigin.Begin);
            return _binaryTree.Read<Wrapper>(_m5)._t1[0];
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _binaryTree.Dispose();
        }

    }
}
