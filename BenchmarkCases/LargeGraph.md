## Large object graph

Protobuf-net 3.0.0-alpha.34 / MessagePack 1.7.3.7 / Ceras 4.1.6 / Apex

MessagePack fares worse here than others because it doesn't track object references

```
BenchmarkDotNet=v0.11.5, OS=Windows 10.0.17134.885 (1803/April2018Update/Redstone4)
Intel Core i5-4690 CPU 3.50GHz (Haswell), 1 CPU, 4 logical and 4 physical cores
Frequency=3417962 Hz, Resolution=292.5720 ns, Timer=TSC
.NET Core SDK=3.0.100-preview7-012821
  Core   : .NET Core 3.0.0-preview7-27912-14 (CoreCLR 4.700.19.32702, CoreFX 4.700.19.36209), 64bit RyuJIT
```

#### Serialization & Deserialization
|        Method |        Mean |      StdDev | Ratio |    Gen 0 |    Gen 1 |    Gen 2 |  Allocated |
|-------------- |------------:|------------:|------:|---------:|---------:|---------:|-----------:|
|    S_Protobuf | 11,895.1 us |  36.0355 us | 17.95 | 125.0000 |        - |        - |  1657119 B |
|    D_Protobuf | 21,661.3 us | 149.7259 us | 32.72 |  62.5000 |  31.2500 |        - |  5865406 B |
| S_MessagePack | 27,984.3 us | 321.6677 us | 42.26 | 437.5000 | 437.5000 | 437.5000 | 16874144 B |
| D_MessagePack | 49,904.7 us | 592.9531 us | 75.34 |        - |        - |        - | 15032168 B |
|       S_Ceras |  5,971.6 us |  18.7161 us |  9.02 |        - |        - |        - |    11128 B |
|       D_Ceras | 14,295.0 us | 135.6455 us | 21.58 |  31.2500 |  15.6250 |        - |  2913496 B |
|        S_Apex |    662.6 us |   0.6263 us |  1.00 |        - |        - |        - |          - |
|        D_Apex |    836.4 us |   8.7684 us |  1.26 |  10.7422 |   1.9531 |        - |   647888 B |
