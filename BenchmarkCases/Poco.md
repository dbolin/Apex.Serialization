## Pocos

Protobuf-net 3.0.0-alpha.43 / MessagePack 1.7.3.7 / NetSerializer 4.1.0 / Ceras 4.1.6 / Apex

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
BenchmarkDotNet=v0.11.5, OS=Windows 10.0.17134.885 (1803/April2018Update/Redstone4)
Intel Core i5-4690 CPU 3.50GHz (Haswell), 1 CPU, 4 logical and 4 physical cores
Frequency=3417962 Hz, Resolution=292.5720 ns, Timer=TSC
.NET Core SDK=3.0.100-preview6-012264
  [Host] : .NET Core 3.0.0-preview6-27804-01 (CoreCLR 4.700.19.30373, CoreFX 4.700.19.30308), 64bit RyuJIT
```

#### Mutable Poco Serialization

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|------:|------:|------:|----------:|
|      Protobuf | 474.56 us | 3.9231 us | 3.6696 us | 35.30 |    0.24 |     - |     - |     - |     314 B |
| NetSerializer | 456.23 us | 1.1548 us | 1.0802 us | 33.92 |    0.11 |     - |     - |     - |         - |
|   MessagePack | 392.81 us | 0.8932 us | 0.8355 us | 29.19 |    0.08 |     - |     - |     - |         - |
|         Ceras | 297.80 us | 0.9457 us | 0.7897 us | 22.14 |    0.09 |     - |     - |     - |      40 B |
|          Apex |  13.45 us | 0.0369 us | 0.0327 us |  1.00 |    0.00 |     - |     - |     - |         - |

#### Mutable Poco Deserialization

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|------:|----------:|
|      Protobuf | 694.31 us | 3.4614 us | 3.0684 us | 23.41 |    0.70 | 8.7891 | 0.9766 |     - | 105.45 KB |
| NetSerializer | 483.23 us | 3.2446 us | 3.0350 us | 16.26 |    0.51 | 7.8125 | 0.9766 |     - |  93.99 KB |
|   MessagePack | 329.37 us | 1.1982 us | 1.1208 us | 11.08 |    0.37 | 9.2773 | 0.9766 |     - |   93.8 KB |
|         Ceras | 339.23 us | 3.0692 us | 2.8709 us | 11.42 |    0.39 | 8.7891 | 0.9766 |     - |   93.8 KB |
|          Apex |  29.63 us | 0.7641 us | 0.8799 us |  1.00 |    0.00 | 7.9956 | 1.1292 |     - |  93.99 KB |

#### Immutable Poco Serialization

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|------:|------:|------:|----------:|
|      Protobuf | 512.50 us | 9.0402 us | 8.4562 us | 37.73 |    0.66 |     - |     - |     - |     315 B |
| NetSerializer | 457.78 us | 0.4459 us | 0.4171 us | 33.70 |    0.08 |     - |     - |     - |         - |
|   MessagePack | 399.55 us | 1.6691 us | 1.4796 us | 29.43 |    0.15 |     - |     - |     - |         - |
|         Ceras | 284.10 us | 1.0728 us | 1.0035 us | 20.92 |    0.10 |     - |     - |     - |      40 B |
|          Apex |  13.58 us | 0.0374 us | 0.0350 us |  1.00 |    0.00 |     - |     - |     - |         - |

#### Immutable Poco Deserialization

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD |   Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|--------:|-------:|------:|----------:|
|      Protobuf | 605.59 us | 1.3097 us | 1.2251 us | 20.28 |    0.41 |  8.7891 | 0.9766 |     - | 105.45 KB |
| NetSerializer | 484.85 us | 3.2039 us | 2.8402 us | 16.23 |    0.35 |  7.8125 | 0.4883 |     - |  93.99 KB |
|   MessagePack | 331.94 us | 0.9729 us | 0.9100 us | 11.12 |    0.23 |  8.7891 | 0.9766 |     - |   93.8 KB |
|         Ceras | 617.96 us | 0.7456 us | 0.6226 us | 20.68 |    0.42 | 16.6016 | 1.9531 |     - | 171.93 KB |
|          Apex |  29.88 us | 0.6949 us | 0.6160 us |  1.00 |    0.00 |  8.0261 | 1.0986 |     - |  93.99 KB |

