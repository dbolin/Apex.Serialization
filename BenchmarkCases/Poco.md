## Pocos

Protobuf-net 3.0.0-alpha.43 / MessagePack 2.0.335 / NetSerializer 4.1.1 / Ceras 4.1.7 / Apex

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
            public readonly string StringProp;      //using the text "hello"
            public readonly int IntProp;            //123
            public readonly Guid GuidProp;          //Guid.NewGuid()
            public readonly DateTime DateProp;      //DateTime.Now
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
BenchmarkDotNet=v0.12.0, OS=Windows 10.0.18362
Intel Core i5-4690 CPU 3.50GHz (Haswell), 1 CPU, 4 logical and 4 physical cores
.NET Core SDK=3.1.101
  [Host]     : .NET Core 3.1.1 (CoreCLR 4.700.19.60701, CoreFX 4.700.19.60801), X64 RyuJIT
```

#### Mutable Poco Serialization

|        Method |      Mean |    Error |   StdDev | Ratio | RatioSD | Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|---------:|---------:|------:|--------:|------:|------:|------:|----------:|
|      Protobuf | 473.50 us | 6.492 us | 6.073 us | 36.35 |    0.45 |     - |     - |     - |     314 B |
| NetSerializer | 420.98 us | 1.721 us | 1.437 us | 32.36 |    0.10 |     - |     - |     - |         - |
|   MessagePack | 372.67 us | 1.011 us | 0.945 us | 28.66 |    0.12 |     - |     - |     - |       1 B |
|         Ceras | 264.49 us | 0.912 us | 0.853 us | 20.34 |    0.09 |     - |     - |     - |      40 B |
|          Apex |  13.01 us | 0.035 us | 0.031 us |  1.00 |    0.00 |     - |     - |     - |         - |

#### Mutable Poco Deserialization

|        Method |      Mean |    Error |   StdDev | Ratio | RatioSD |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|---------:|---------:|------:|--------:|-------:|-------:|------:|----------:|
|      Protobuf | 598.17 us | 2.484 us | 2.324 us | 21.09 |    0.13 | 6.8359 | 0.9766 |     - | 105.44 KB |
| NetSerializer | 455.12 us | 0.960 us | 0.898 us | 16.05 |    0.10 | 6.3477 | 0.9766 |     - |  93.99 KB |
|   MessagePack | 451.09 us | 2.873 us | 2.687 us | 15.92 |    0.12 | 6.3477 | 0.4883 |     - |  93.81 KB |
|         Ceras | 313.05 us | 0.756 us | 0.670 us | 11.04 |    0.08 | 6.3477 | 0.4883 |     - |  93.81 KB |
|          Apex |  28.35 us | 0.201 us | 0.178 us |  1.00 |    0.00 | 6.7444 | 1.0071 |     - |  93.99 KB |

#### Immutable Poco Serialization

|        Method |      Mean |    Error |   StdDev | Ratio | RatioSD | Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|---------:|---------:|------:|--------:|------:|------:|------:|----------:|
|      Protobuf | 459.12 us | 1.416 us | 1.325 us | 33.49 |    0.13 |     - |     - |     - |     314 B |
| NetSerializer | 423.13 us | 1.436 us | 1.343 us | 30.86 |    0.14 |     - |     - |     - |         - |
|   MessagePack | 366.59 us | 0.828 us | 0.775 us | 26.74 |    0.07 |     - |     - |     - |       1 B |
|         Ceras | 250.09 us | 0.995 us | 0.931 us | 18.24 |    0.08 |     - |     - |     - |      41 B |
|          Apex |  13.71 us | 0.029 us | 0.027 us |  1.00 |    0.00 |     - |     - |     - |         - |

#### Immutable Poco Deserialization

|        Method |      Mean |    Error |   StdDev | Ratio | RatioSD |   Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|---------:|---------:|------:|--------:|--------:|-------:|------:|----------:|
|      Protobuf | 627.17 us | 2.944 us | 2.609 us | 21.56 |    0.11 |  6.8359 |      - |     - | 105.44 KB |
| NetSerializer | 450.55 us | 1.444 us | 1.280 us | 15.49 |    0.08 |  6.3477 | 0.4883 |     - |  93.99 KB |
|   MessagePack | 453.53 us | 0.936 us | 0.830 us | 15.59 |    0.05 |  6.3477 | 0.4883 |     - |  93.81 KB |
|         Ceras | 593.53 us | 2.421 us | 2.147 us | 20.40 |    0.11 | 11.7188 | 0.9766 |     - | 171.93 KB |
|          Apex |  29.09 us | 0.123 us | 0.109 us |  1.00 |    0.00 |  6.7444 | 0.8850 |     - |  93.99 KB |

