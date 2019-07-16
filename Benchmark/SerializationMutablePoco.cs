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
    public class SerializationMutablePoco
    {
        [ProtoContract]
        [MessagePackObject]
        [Serializable]
        public class Poco
        {
            [Key(0)]
            [ProtoMember(1)]
            public string StringProp { get; set; }      //using the text "hello"
            [Key(1)]
            [ProtoMember(2)]
            public int IntProp { get; set; }            //123
            [Key(2)]
            [ProtoMember(3)]
            public Guid GuidProp { get; set; }          //Guid.NewGuid()
            [Key(3)]
            [ProtoMember(4)]
            public DateTime DateProp { get; set; }      //DateTime.Now
        }

        private IBinary _binary = Binary.Create();
        //private Serializer _hyperion = new Serializer();
        private NetSerializer.Serializer _netSerializer = new NetSerializer.Serializer(new[] { typeof(List<Poco>) });

        private MemoryStream _m1 = new MemoryStream();
        private MemoryStream _m2 = new MemoryStream();
        private MemoryStream _m3 = new MemoryStream();
        private MemoryStream _m4 = new MemoryStream();
        private MemoryStream _m5 = new MemoryStream();

        private List<Poco> _t1 = new List<Poco>();

        public SerializationMutablePoco()
        {
            for (int i = 0; i < 1000; ++i)
            {
                _t1.Add(new Poco { StringProp = "hello", IntProp = 123, GuidProp = Guid.NewGuid(),  DateProp = DateTime.Now});
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
