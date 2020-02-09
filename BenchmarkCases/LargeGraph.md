## Large object graph

Protobuf-net 3.0.0-alpha.34 / MessagePack 2.0.335 / Ceras 4.1.7 / Apex

MessagePack fares worse here than others because it doesn't track object references

```
BenchmarkDotNet=v0.12.0, OS=Windows 10.0.18362
Intel Core i5-4690 CPU 3.50GHz (Haswell), 1 CPU, 4 logical and 4 physical cores
.NET Core SDK=3.1.101
  [Host]     : .NET Core 3.1.1 (CoreCLR 4.700.19.60701, CoreFX 4.700.19.60801), X64 RyuJIT
```

#### Serialization & Deserialization
|        Method |        Mean |     Error |    StdDev |  Ratio | RatioSD |    Gen 0 |   Gen 1 | Gen 2 |  Allocated |
|-------------- |------------:|----------:|----------:|-------:|--------:|---------:|--------:|------:|-----------:|
|    S_Protobuf | 11,640.7 us | 145.82 us | 136.40 us |  18.35 |    0.21 | 109.3750 |       - |     - |  1655548 B |
|    D_Protobuf | 21,661.2 us | 164.21 us | 145.57 us |  34.17 |    0.26 |  62.5000 | 31.2500 |     - |  5865406 B |
| S_MessagePack | 28,070.3 us |  57.21 us |  53.52 us |  44.29 |    0.22 |        - |       - |     - |   227040 B |
| D_MessagePack | 63,625.0 us | 750.98 us | 702.46 us | 100.33 |    1.15 |        - |       - |     - | 15032263 B |
|       S_Ceras |  5,815.5 us |  10.47 us |   9.28 us |   9.18 |    0.05 |        - |       - |     - |    11138 B |
|       D_Ceras | 14,154.6 us |  80.27 us |  75.09 us |  22.34 |    0.17 |  31.2500 | 15.6250 |     - |  2913496 B |
|        S_Apex |    633.8 us |   3.13 us |   2.78 us |   1.00 |    0.00 |        - |       - |     - |        1 B |
|        D_Apex |    812.9 us |   4.77 us |   4.46 us |   1.28 |    0.01 |  12.6953 |  2.9297 |     - |   652304 B |
