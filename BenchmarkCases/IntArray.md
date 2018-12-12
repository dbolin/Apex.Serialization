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
      Protobuf | 594.50 us | 1.1629 us | 1.0309 us | 32.08 |    0.06 |           - |           - |           - |               209 B |
   MessagePack |  94.43 us | 0.3269 us | 0.3058 us |  5.10 |    0.02 |           - |           - |           - |                   - |
      Hyperion |  96.50 us | 1.0681 us | 0.9991 us |  5.21 |    0.06 |     19.6533 |     19.5313 |     19.5313 |            200343 B |
 ZeroFormatter |  90.32 us | 0.3366 us | 0.3149 us |  4.87 |    0.02 |     20.0195 |     20.0195 |     20.0195 |            200236 B |
          Apex |  18.53 us | 0.0348 us | 0.0325 us |  1.00 |    0.00 |           - |           - |           - |                   - |
```

#### Deserialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 900.16 us | 5.7271 us | 5.3571 us |  9.56 |    0.09 |    197.2656 |    155.2734 |    155.2734 |          1099.19 KB |
   MessagePack | 270.97 us | 1.8207 us | 1.7031 us |  2.88 |    0.03 |     61.5234 |     61.5234 |     61.5234 |            391.2 KB |
      Hyperion | 204.11 us | 3.7980 us | 3.7301 us |  2.17 |    0.05 |     20.0195 |     20.0195 |     20.0195 |           391.03 KB |
 ZeroFormatter | 117.17 us | 0.5811 us | 0.5435 us |  1.24 |    0.01 |     20.6299 |     20.5078 |     20.5078 |           390.99 KB |
          Apex |  94.18 us | 0.8383 us | 0.7841 us |  1.00 |    0.00 |     17.9443 |     17.9443 |     17.9443 |           195.52 KB |
```
