## int[50000]

Protobuf-net 2.4.0 / Hyperion 0.9.8 / MessagePack 1.7.3.4 / ZeroFormatter 1.6.4 / Apex

Serializing/deserializing the object defined as follows

```csharp
        [ProtoContract]
        [MessagePackObject]
        [ZeroFormattable]
        public class Wrapper
        {
            [ProtoMember(1)]
            [Key(0)]
            [Index(0)]
            public virtual int[] _t1 { get; set; } = new int[50000];
        }
```

The first element of the int[] is returned from the test method after deserializing.

```
BenchmarkDotNet=v0.11.3, OS=Windows 10.0.17134.407 (1803/April2018Update/Redstone4)
Intel Core i5-4690 CPU 3.50GHz (Haswell), 1 CPU, 4 logical and 4 physical cores
Frequency=3417967 Hz, Resolution=292.5716 ns, Timer=TSC
.NET Core SDK=2.2.100
  [Host] : .NET Core 2.2.0 (CoreCLR 4.6.27110.04, CoreFX 4.6.27110.04), 64bit RyuJIT
```

#### Serialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 652.14 us | 7.8039 us | 7.2998 us | 34.70 |    0.42 |           - |           - |           - |               209 B |
   MessagePack |  95.26 us | 0.6104 us | 0.5710 us |  5.07 |    0.04 |           - |           - |           - |                   - |
      Hyperion |  63.13 us | 1.0145 us | 0.8994 us |  3.36 |    0.05 |     24.7803 |     24.6582 |     24.6582 |            200389 B |
 ZeroFormatter |  57.93 us | 0.7141 us | 0.6680 us |  3.08 |    0.04 |     28.9307 |     28.8086 |     28.8086 |            200310 B |
          Apex |  18.80 us | 0.1222 us | 0.1143 us |  1.00 |    0.00 |           - |           - |           - |                   - |
```

#### Deserialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 853.62 us | 8.1080 us | 7.5843 us | 13.37 |    0.17 |    199.2188 |    156.2500 |    156.2500 |          1099.16 KB |
   MessagePack | 253.74 us | 2.2734 us | 2.1266 us |  3.97 |    0.05 |     59.0820 |     59.0820 |     59.0820 |           390.99 KB |
      Hyperion | 133.24 us | 0.7542 us | 0.7054 us |  2.08 |    0.03 |     19.0430 |     19.0430 |     19.0430 |           391.02 KB |
 ZeroFormatter |  71.72 us | 1.0080 us | 0.9429 us |  1.12 |    0.02 |     19.4092 |     19.2871 |     19.2871 |           390.97 KB |
          Apex |  63.94 us | 0.7449 us | 0.5815 us |  1.00 |    0.00 |     19.4092 |     19.4092 |     19.4092 |           195.49 KB |
```
