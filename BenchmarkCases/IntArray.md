## int[50000]

Protobuf-net 2.4.0 / MessagePack 1.7.3.7 / Apex

Serializing/deserializing the object defined as follows

```csharp
        [ProtoContract]
        [MessagePackObject]
        public class Wrapper
        {
            [ProtoMember(1)]
            [Key(0)]
            public int[] _t1 = new int[50000];
        }
```

The first element of the int[] is returned from the test method after deserializing.

```
BenchmarkDotNet=v0.11.5, OS=Windows 10.0.17134.885 (1803/April2018Update/Redstone4)
Intel Core i5-4690 CPU 3.50GHz (Haswell), 1 CPU, 4 logical and 4 physical cores
Frequency=3417962 Hz, Resolution=292.5720 ns, Timer=TSC
.NET Core SDK=3.0.100-preview6-012264
  [Host] : .NET Core 3.0.0-preview6-27804-01 (CoreCLR 4.700.19.30373, CoreFX 4.700.19.30308), 64bit RyuJIT
```

#### Serialization
```
|      Method |       Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------ |-----------:|----------:|----------:|------:|--------:|------:|------:|------:|----------:|
|    Protobuf | 610.619 us | 1.9477 us | 1.8219 us | 79.65 |    0.37 |     - |     - |     - |     209 B |
| MessagePack |  90.922 us | 0.8405 us | 0.7862 us | 11.86 |    0.12 |     - |     - |     - |         - |
|        Apex |   7.666 us | 0.0320 us | 0.0299 us |  1.00 |    0.00 |     - |     - |     - |         - |
```

#### Deserialization
```
|      Method |       Mean |     Error |    StdDev | Ratio | RatioSD |    Gen 0 |    Gen 1 |    Gen 2 | Allocated |
|------------ |-----------:|----------:|----------:|------:|--------:|---------:|---------:|---------:|----------:|
|    Protobuf | 1,372.0 us | 27.247 us | 34.459 us |  9.18 |    0.33 | 105.4688 | 103.5156 | 103.5156 | 1098.5 KB |
| MessagePack |   365.8 us |  7.216 us | 11.652 us |  2.46 |    0.10 |  44.4336 |  44.4336 |  44.4336 | 390.82 KB |
|        Apex |   148.8 us |  2.996 us |  4.574 us |  1.00 |    0.00 |  21.9727 |  21.9727 |  21.9727 | 195.49 KB |
```
