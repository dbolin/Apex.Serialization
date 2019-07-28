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

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|-------:|------:|------:|----------:|
|      Protobuf | 472.46 us | 5.0713 us | 4.7437 us | 31.20 |    0.39 | 2.9297 |     - |     - |   40250 B |
| NetSerializer | 459.01 us | 2.0165 us | 1.8862 us | 30.31 |    0.26 |      - |     - |     - |         - |
|   MessagePack | 401.39 us | 1.3113 us | 1.2266 us | 26.51 |    0.16 |      - |     - |     - |         - |
|          Apex |  15.14 us | 0.1129 us | 0.1056 us |  1.00 |    0.00 |      - |     - |     - |         - |


#### Mutable Poco Deserialization

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|------:|----------:|
|      Protobuf | 677.98 us | 5.2856 us | 4.4137 us | 21.53 |    0.29 | 8.7891 | 0.9766 |     - | 105.44 KB |
| NetSerializer | 485.84 us | 2.2207 us | 2.0773 us | 15.42 |    0.14 | 7.8125 |      - |     - |  93.99 KB |
|   MessagePack | 343.44 us | 1.4142 us | 1.2537 us | 10.90 |    0.12 | 7.8125 | 0.9766 |     - |   93.8 KB |
|          Apex |  31.51 us | 0.3831 us | 0.3396 us |  1.00 |    0.00 | 8.0261 | 1.0681 |     - |  93.99 KB |


#### Immutable Poco Serialization

|        Method |      Mean |      Error |     StdDev |    Median | Ratio | RatioSD |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|-----------:|-----------:|----------:|------:|--------:|-------:|------:|------:|----------:|
|      Protobuf | 552.48 us | 13.0233 us | 37.7829 us | 541.94 us | 35.30 |    2.89 | 2.9297 |     - |     - |   40254 B |
| NetSerializer | 495.71 us |  1.9670 us |  1.8399 us | 495.79 us | 31.38 |    0.15 |      - |     - |     - |         - |
|   MessagePack | 435.26 us |  1.9975 us |  1.8685 us | 435.11 us | 27.55 |    0.18 |      - |     - |     - |         - |
|          Apex |  15.80 us |  0.0813 us |  0.0760 us |  15.81 us |  1.00 |    0.00 |      - |     - |     - |         - |


#### Immutable Poco Deserialization

|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|------:|----------:|
|      Protobuf | 683.50 us | 2.4625 us | 2.3034 us | 22.35 |    0.18 | 8.7891 |      - |     - | 105.44 KB |
| NetSerializer | 476.15 us | 2.0951 us | 1.6357 us | 15.56 |    0.13 | 7.8125 | 0.9766 |     - |  93.99 KB |
|   MessagePack | 344.13 us | 2.8351 us | 2.6520 us | 11.25 |    0.12 | 8.3008 | 0.9766 |     - |   93.8 KB |
|          Apex |  30.60 us | 0.2462 us | 0.2056 us |  1.00 |    0.00 | 7.9956 | 1.0986 |     - |  93.99 KB |


