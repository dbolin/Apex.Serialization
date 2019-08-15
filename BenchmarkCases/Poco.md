## Pocos

Protobuf-net 3.0.0-alpha.43 / MessagePack 1.7.3.7 / NetSerializer 4.1.0 / Apex

Serializing/deserializing a list of 1000 small pocos defined as follows

```csharp
        public sealed class ImmutablePoco
        {
            public ImmutablePoco(string s, int i, Guid g, DateTime d)
            {
                StringProp = s;
                IntProp = i;
                GuidProp = g;
                DateProp = d;
            }
            public string StringProp { get; }      //using the text "hello"
            public int IntProp { get; }            //123
            public Guid GuidProp { get; }          //Guid.NewGuid()
            public DateTime DateProp { get; }      //DateTime.Now
        }

        public class Poco
        {
            public string StringProp { get; set; }      //using the text "hello"
            public int IntProp { get; set; }            //123
            public Guid GuidProp { get; set; }          //Guid.NewGuid()
            public DateTime DateProp { get; set; }      //DateTime.Now
        }
```

```
BenchmarkDotNet=v0.11.5, OS=Windows 10.0.17134.885 (1803/April2018Update/Redstone4)
Intel Core i5-4690 CPU 3.50GHz (Haswell), 1 CPU, 4 logical and 4 physical cores
Frequency=3417962 Hz, Resolution=292.5720 ns, Timer=TSC
.NET Core SDK=3.0.100-preview6-012264
  [Host] : .NET Core 3.0.0-preview6-27804-01 (CoreCLR 4.700.19.30373, CoreFX 4.700.19.30308), 64bit RyuJIT
```

#### Mutable Poco Serialization

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|------:|------:|------:|----------:|
|      Protobuf | 499.45 us | 1.1651 us | 1.0898 us | 36.42 |    0.09 |     - |     - |     - |     315 B |
| NetSerializer | 454.63 us | 1.3409 us | 1.1197 us | 33.17 |    0.09 |     - |     - |     - |         - |
|   MessagePack | 397.93 us | 2.8081 us | 2.6267 us | 29.02 |    0.21 |     - |     - |     - |         - |
|          Apex |  13.71 us | 0.0285 us | 0.0267 us |  1.00 |    0.00 |     - |     - |     - |         - |

#### Mutable Poco Deserialization

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|------:|----------:|
|      Protobuf | 613.62 us | 3.6265 us | 3.2148 us | 20.46 |    0.43 | 8.7891 | 0.9766 |     - | 105.45 KB |
| NetSerializer | 476.83 us | 2.7650 us | 2.4511 us | 15.90 |    0.34 | 7.8125 | 0.9766 |     - |  93.99 KB |
|   MessagePack | 328.10 us | 0.7863 us | 0.6970 us | 10.94 |    0.22 | 9.2773 | 0.9766 |     - |   93.8 KB |
|          Apex |  29.95 us | 0.6339 us | 0.6225 us |  1.00 |    0.00 | 8.0261 | 1.1292 |     - |  93.99 KB |

#### Immutable Poco Serialization

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|------:|------:|------:|----------:|
|      Protobuf | 464.65 us | 3.1857 us | 2.9799 us | 33.89 |    0.20 |     - |     - |     - |     314 B |
| NetSerializer | 453.09 us | 2.5367 us | 2.2488 us | 33.03 |    0.17 |     - |     - |     - |         - |
|   MessagePack | 396.31 us | 1.7824 us | 1.6672 us | 28.91 |    0.15 |     - |     - |     - |         - |
|          Apex |  13.72 us | 0.0282 us | 0.0250 us |  1.00 |    0.00 |     - |     - |     - |         - |

#### Immutable Poco Deserialization

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|------:|----------:|
|      Protobuf | 610.15 us | 8.2600 us | 7.7264 us | 20.58 |    0.36 | 8.7891 | 0.9766 |     - | 105.45 KB |
| NetSerializer | 469.99 us | 1.2918 us | 1.2083 us | 15.86 |    0.39 | 7.8125 | 0.9766 |     - |  93.99 KB |
|   MessagePack | 326.40 us | 1.4267 us | 1.2648 us | 11.01 |    0.30 | 9.2773 | 0.9766 |     - |   93.8 KB |
|          Apex |  29.65 us | 0.8216 us | 0.7686 us |  1.00 |    0.00 | 8.0261 | 1.0681 |     - |  93.99 KB |

