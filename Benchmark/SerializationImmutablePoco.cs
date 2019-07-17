using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Apex.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using MessagePack;
using ProtoBuf;
using Serializer = Hyperion.Serializer;

namespace Benchmark
{
    [MemoryDiagnoser]
    public class SerializationImmutablePoco
    {
        [ProtoContract]
        [MessagePackObject]
        [Serializable]
        public sealed class ImmutablePoco
        {
            public ImmutablePoco(string s, int i, Guid g, DateTime d)
            {
                StringProp = s;
                IntProp = i;
                GuidProp = g;
                DateProp = d;
            }
            [Key(0)]
            [ProtoMember(1)]
            public string StringProp { get; }      //using the text "hello"
            [Key(1)]
            [ProtoMember(2)]
            public int IntProp { get; }            //123
            [Key(2)]
            [ProtoMember(3)]
            public Guid GuidProp { get; }          //Guid.NewGuid()
            [Key(3)]
            [ProtoMember(4)]
            public DateTime DateProp { get; }      //DateTime.Now
        }

        private IBinary _binary = Binary.Create();
        //private Serializer _hyperion = new Serializer();
        private NetSerializer.Serializer _netSerializer = new NetSerializer.Serializer(new[] { typeof(List<ImmutablePoco>) });

        private MemoryStream _m1 = new MemoryStream();
        private MemoryStream _m2 = new MemoryStream();
        private MemoryStream _m3 = new MemoryStream();
        private MemoryStream _m4 = new MemoryStream();
        private MemoryStream _m5 = new MemoryStream();

        private List<ImmutablePoco> _t1 = new List<ImmutablePoco>();

        public SerializationImmutablePoco()
        {
            for (int i = 0; i < 1000; ++i)
            {
                _t1.Add(new ImmutablePoco("hello", 123, Guid.NewGuid(), DateTime.Now));
            }

            _binary.Write(_t1, _m1);
            //_hyperion.Serialize(_t1, _m2);
            ProtoBuf.Serializer.Serialize(_m3, _t1);
            MessagePackSerializer.Serialize(_m4, _t1);
            _netSerializer.Serialize(_m5, _t1);
        }

        [Benchmark]
        public void Protobuf()
        {
            _m3.Seek(0, SeekOrigin.Begin);
            ProtoBuf.Serializer.Serialize(_m3, _t1);
        }

        [Benchmark]
        public void NetSerializer()
        {
            _m5.Seek(0, SeekOrigin.Begin);
            _netSerializer.Serialize(_m5, _t1);
        }

        [Benchmark]
        public void MessagePack()
        {
            _m4.Seek(0, SeekOrigin.Begin);
            MessagePackSerializer.Serialize(_m4, _t1);
        }

        /*
        [Benchmark]
        public void Hyperion()
        {
            _m2.Seek(0, SeekOrigin.Begin);
            _hyperion.Serialize(_t1, _m2);
        }
        */

        [Benchmark(Baseline = true)]
        public void Apex()
        {
            _m1.Seek(0, SeekOrigin.Begin);
            _binary.Write(_t1, _m1);
        }
    }
}
