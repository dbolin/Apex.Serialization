## int[50000]

Protobuf-net 3.0.0-alpha.34 / MessagePack 2.0.335 / Apex

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
BenchmarkDotNet=v0.12.0, OS=Windows 10.0.18362
Intel Core i5-4690 CPU 3.50GHz (Haswell), 1 CPU, 4 logical and 4 physical cores
.NET Core SDK=3.1.101
  [Host]     : .NET Core 3.1.1 (CoreCLR 4.700.19.60701, CoreFX 4.700.19.60801), X64 RyuJIT
```

#### Serialization
```
|      Method |       Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------ |-----------:|----------:|----------:|------:|--------:|------:|------:|------:|----------:|
|    Protobuf | 670.970 us | 2.4391 us | 2.1622 us | 85.15 |    0.32 |     - |     - |     - |     218 B |
| MessagePack | 121.629 us | 0.1927 us | 0.1803 us | 15.44 |    0.05 |     - |     - |     - |         - |
|        Apex |   7.879 us | 0.0215 us | 0.0201 us |  1.00 |    0.00 |     - |     - |     - |         - |
```

#### Deserialization
```
|      Method |       Mean |    Error |   StdDev | Ratio | RatioSD |    Gen 0 |    Gen 1 |    Gen 2 | Allocated |
|------------ |-----------:|---------:|---------:|------:|--------:|---------:|---------:|---------:|----------:|
|    Protobuf | 1,442.3 us | 28.13 us | 42.10 us | 11.08 |    0.49 | 103.5156 | 101.5625 | 101.5625 | 1098.4 KB |
| MessagePack |   753.1 us | 15.02 us | 16.07 us |  5.82 |    0.26 |  43.9453 |  43.9453 |  43.9453 | 390.74 KB |
|        Apex |   130.3 us |  2.57 us |  4.07 us |  1.00 |    0.00 |  17.5781 |  17.5781 |  17.5781 |  195.5 KB |
```
