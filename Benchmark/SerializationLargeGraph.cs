using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Ceras;
using Ceras.ImmutableCollections;
using FluentAssertions;
using MessagePack;
using MessagePack.ImmutableCollection;
using MessagePack.Resolvers;
using ProtoBuf;
using Serializer = Hyperion.Serializer;

namespace Benchmark
{
    [MemoryDiagnoser]
    public class SerializationLargeGraph
    {
        [ProtoContract(AsReferenceDefault = true, SkipConstructor = true)]
        [MessagePackObject]
        [Serializable]
        public sealed class TopLevel
        {
            public TopLevel(Guid id, ImmutableSortedDictionary<string, FirstLevelChild> children, int version, string tag, string description)
            {
                Id = id;
                Children = children;
                Version = version;
                Tag = tag;
                Description = description;
            }

            [Key(0)]
            [ProtoMember(1)]
            public readonly Guid Id;

            [Key(1)]
            [ProtoMember(2)]
            public readonly ImmutableSortedDictionary<string, FirstLevelChild> Children;

            [Key(2)]
            [ProtoMember(3)]
            public readonly int Version;

            [Key(3)]
            [ProtoMember(4)]
            public readonly string Tag;

            [Key(4)]
            [ProtoMember(5)]
            public readonly string Description;
        }

        [ProtoContract(AsReferenceDefault = true, SkipConstructor = true)]
        [MessagePackObject]
        [Serializable]
        public sealed class FirstLevelChild
        {
            public FirstLevelChild(Guid id, ImmutableSortedDictionary<Guid, SecondLevelChild> children, decimal weighting, decimal? error, Guid? referenceId)
            {
                Id = id;
                Children = children;
                Weighting = weighting;
                Error = error;
                ReferenceId = referenceId;
            }

            [Key(0)]
            [ProtoMember(1)]
            public readonly Guid Id;

            [Key(1)]
            [ProtoMember(2)]
            public readonly ImmutableSortedDictionary<Guid, SecondLevelChild> Children;

            [Key(2)]
            [ProtoMember(3)]
            public readonly decimal Weighting;

            [Key(3)]
            [ProtoMember(4)]
            public readonly decimal? Error;

            [Key(4)]
            [ProtoMember(5)]
            public readonly Guid? ReferenceId;
        }

        [ProtoContract(AsReferenceDefault = true, SkipConstructor = true)]
        [MessagePackObject]
        [Serializable]
        public sealed class SecondLevelChild
        {
            public SecondLevelChild(Guid id, string name, decimal? decimalValue, string stringValue, DateTime createdDateTime)
            {
                Id = id;
                Name = name;
                DecimalValue = decimalValue;
                StringValue = stringValue;
                CreatedDateTime = createdDateTime;
            }

            [Key(0)]
            [ProtoMember(1)]
            public readonly Guid Id;

            [Key(1)]
            [ProtoMember(2)]
            public readonly string Name;

            [Key(2)]
            [ProtoMember(3)]
            public readonly decimal? DecimalValue;

            [Key(3)]
            [ProtoMember(4)]
            public readonly string? StringValue;

            [Key(4)]
            [ProtoMember(5)]
            public readonly DateTime CreatedDateTime;
        }

        private IBinary _binary = Binary.Create(new Settings { SerializationMode = Mode.Graph });
        //private Serializer _hyperion = new Serializer();
        //private NetSerializer.Serializer _netSerializer = new NetSerializer.Serializer(new[] { typeof(List<TopLevel>) });
        private readonly CerasSerializer ceras;
        private byte[] b = new byte[16];

        private MemoryStream _m1 = new MemoryStream();
        private MemoryStream _m2 = new MemoryStream();
        private MemoryStream _m3 = new MemoryStream();
        private MemoryStream _m4 = new MemoryStream();
        private MemoryStream _m5 = new MemoryStream();

        private List<TopLevel> _t1 = new List<TopLevel>();

        public SerializationLargeGraph()
        {
            var initial = new TopLevel(Guid.NewGuid(),
                ImmutableSortedDictionary<string, FirstLevelChild>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
                0, "Tag", "Description");

            var current = initial;
            for (int i = 0; i < 1000; ++i)
            {
                if(i % 10 == 0)
                {
                    _t1.Add(current);
                }

                if(i % 10 == 0)
                {
                    current = AddFirstLevelChild(current);
                }

                current = AddSecondLevelChild(current);
            }

            var config = new SerializerConfig { DefaultTargets = TargetMember.AllFields, PreserveReferences = true };
            config.Advanced.ReadonlyFieldHandling = ReadonlyFieldHandling.ForcedOverwrite;
            config.OnConfigNewType = tc => tc.TypeConstruction = TypeConstruction.ByUninitialized();
            config.UseImmutableFormatters();
            ceras = new CerasSerializer(config);

            //_hyperion.Serialize(_t1, _m2);
            CompositeResolver.RegisterAndSetAsDefault(
                ImmutableCollectionResolver.Instance,
                DynamicObjectResolver.Instance,
                StandardResolver.Instance
                );
            //_netSerializer.Serialize(_m5, _t1);
        }

        private TopLevel AddFirstLevelChild(TopLevel current)
        {
            var i = current.Children.Count;

            return new TopLevel(
                current.Id,
                current.Children.SetItem($"Key{i}",
                    new FirstLevelChild(
                        Guid.NewGuid(),
                        ImmutableSortedDictionary<Guid, SecondLevelChild>.Empty,
                        i,
                        (i % 2 == 0) ? i : (decimal?)null,
                        (i % 2 == 0) ? Guid.NewGuid() : (Guid?)null
                        )),
                current.Version + 1,
                current.Tag,
                current.Description
                );
        }

        private TopLevel AddSecondLevelChild(TopLevel current)
        {
            var childKvp = current.Children.Last();
            var newSecondLevelChild = new SecondLevelChild(Guid.NewGuid(), childKvp.Value.Id.ToString(), null, childKvp.Key, DateTime.UtcNow);
            var newChild = new FirstLevelChild(
                    childKvp.Value.Id,
                    childKvp.Value.Children.Add(newSecondLevelChild.Id, newSecondLevelChild),
                    childKvp.Value.Weighting,
                    childKvp.Value.Error,
                    childKvp.Value.ReferenceId
                );
            return new TopLevel(
                    current.Id,
                    current.Children.SetItem(childKvp.Key, newChild),
                    current.Version + 1,
                    current.Tag,
                    current.Description
                );
        }

        [GlobalSetup(Targets = new[] { nameof(S_Protobuf), nameof(D_Protobuf) })]
        public void SetupProtobuf()
        {
            S_Protobuf();
        }

        [Benchmark]
        public void S_Protobuf()
        {
            _m3.Seek(0, SeekOrigin.Begin);
            ProtoBuf.Serializer.Serialize(_m3, _t1);
        }

        [Benchmark]
        public object D_Protobuf()
        {
            _m3.Seek(0, SeekOrigin.Begin);
            return ProtoBuf.Serializer.Deserialize<List<TopLevel>>(_m3);
        }

        /*
        [Benchmark]
        public void NetSerializer()
        {
            _m5.Seek(0, SeekOrigin.Begin);
            _netSerializer.Serialize(_m5, _t1);
        }
        */

        [GlobalSetup(Targets = new[] { nameof(S_MessagePack), nameof(D_MessagePack) })]
        public void SetupMessagePack()
        {
            S_MessagePack();
        }

        [Benchmark]
        public void S_MessagePack()
        {
            _m4.Seek(0, SeekOrigin.Begin);
            MessagePackSerializer.Serialize(_m4, _t1);
        }

        [Benchmark]
        public object D_MessagePack()
        {
            _m4.Seek(0, SeekOrigin.Begin);
            return MessagePackSerializer.Deserialize<List<TopLevel>>(_m4);
        }

        [GlobalSetup(Targets = new[] { nameof(S_Ceras), nameof(D_Ceras) })]
        public void SetupCeras()
        {
            S_Ceras();
        }

        [Benchmark]
        public void S_Ceras()
        {
            ceras.Serialize(_t1, ref b);
        }

        [Benchmark]
        public object D_Ceras()
        {
            return ceras.Deserialize<List<TopLevel>>(b);
        }

        /*
        [Benchmark]
        public void Hyperion()
        {
            _m2.Seek(0, SeekOrigin.Begin);
            _hyperion.Serialize(_t1, _m2);
        }
        */

        [GlobalSetup(Targets = new[] { nameof(S_Apex), nameof(D_Apex) })]
        public void SetupApex()
        {
            S_Apex();
        }

        [Benchmark(Baseline = true)]
        public void S_Apex()
        {
            _m1.Seek(0, SeekOrigin.Begin);
            _binary.Write(_t1, _m1);
        }

        [Benchmark]
        public object D_Apex()
        {
            _m1.Seek(0, SeekOrigin.Begin);
            return _binary.Read<List<TopLevel>>(_m1);
        }
    }
}
