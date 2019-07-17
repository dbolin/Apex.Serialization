## Pocos

Protobuf-net 2.4.0 / MessagePack 1.7.3.7 / NetSerializer 4.1.0 / Apex

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
```
|        Method |      Mean |      Error |     StdDev | Ratio | RatioSD |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|-----------:|-----------:|------:|--------:|-------:|------:|------:|----------:|
|      Protobuf | 541.28 us | 10.9697 us | 24.3081 us | 16.56 |    0.57 | 2.9297 |     - |     - |   40251 B |
| NetSerializer | 508.90 us |  2.1397 us |  2.0015 us | 15.72 |    0.08 |      - |     - |     - |         - |
|   MessagePack | 431.69 us |  1.5230 us |  1.4246 us | 13.34 |    0.08 |      - |     - |     - |         - |
|          Apex |  32.37 us |  0.1636 us |  0.1530 us |  1.00 |    0.00 |      - |     - |     - |         - |
```

#### Mutable Poco Deserialization
```
|        Method |      Mean |    Error |   StdDev | Ratio | RatioSD |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|---------:|---------:|------:|--------:|-------:|-------:|------:|----------:|
|      Protobuf |        NA |       NA |       NA |     ? |       ? |      - |      - |     - |         - |
| NetSerializer | 524.59 us | 2.537 us | 2.249 us | 10.15 |    0.27 | 6.8359 | 0.9766 |     - |   96248 B |
|   MessagePack | 360.78 us | 2.634 us | 2.335 us |  6.98 |    0.19 | 7.8125 | 0.4883 |     - |   96056 B |
|          Apex |  51.97 us | 1.026 us | 1.182 us |  1.00 |    0.00 | 7.9956 | 0.9766 |     - |   96248 B |
```

#### Immutable Poco Serialization
```
|        Method |      Mean |      Error |     StdDev |    Median | Ratio | RatioSD |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|-----------:|-----------:|----------:|------:|--------:|-------:|------:|------:|----------:|
|      Protobuf | 552.48 us | 13.0233 us | 37.7829 us | 541.94 us | 35.30 |    2.89 | 2.9297 |     - |     - |   40254 B |
| NetSerializer | 495.71 us |  1.9670 us |  1.8399 us | 495.79 us | 31.38 |    0.15 |      - |     - |     - |         - |
|   MessagePack | 435.26 us |  1.9975 us |  1.8685 us | 435.11 us | 27.55 |    0.18 |      - |     - |     - |         - |
|          Apex |  15.80 us |  0.0813 us |  0.0760 us |  15.81 us |  1.00 |    0.00 |      - |     - |     - |         - |
```

#### Immutable Poco Deserialization
```
|        Method |      Mean |     Error |    StdDev | Ratio | RatioSD |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|------:|----------:|
|      Protobuf |        NA |        NA |        NA |     ? |       ? |      - |      - |     - |         - |
| NetSerializer | 616.18 us | 13.897 us | 40.975 us |  7.22 |    0.55 | 6.8359 |      - |     - |   96248 B |
|   MessagePack | 474.13 us | 22.785 us | 66.102 us |  5.58 |    0.52 | 7.3242 | 0.4883 |     - |   96056 B |
|          Apex |  85.71 us |  1.699 us |  3.432 us |  1.00 |    0.00 | 7.8125 | 0.4883 |     - |   96248 B |
```

### Linux

```
BenchmarkDotNet=v0.11.3, OS=ubuntu 18.04
Intel Core i5-4570 CPU 3.20GHz (Haswell), 1 CPU, 4 logical and 4 physical cores
.NET Core SDK=2.2.101
  [Host] : .NET Core 2.2.0 (CoreCLR 4.6.27110.04, CoreFX 4.6.27110.04), 64bit RyuJIT
```

#### Mutable Poco Serialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 618.82 us | 8.0631 us | 7.5422 us | 17.80 |    0.22 |     12.6953 |           - |           - |             40251 B |
 NetSerializer | 634.77 us | 0.1683 us | 0.1492 us | 18.26 |    0.01 |           - |           - |           - |                   - |
   MessagePack | 581.61 us | 0.0927 us | 0.0723 us | 16.73 |    0.01 |           - |           - |           - |                   - |
      Hyperion | 193.85 us | 0.0509 us | 0.0451 us |  5.58 |    0.00 |     12.6953 |           - |           - |             40304 B |
          Apex |  34.76 us | 0.0082 us | 0.0076 us |  1.00 |    0.00 |           - |           - |           - |                   - |
```

#### Mutable Poco Deserialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 960.85 us | 1.4926 us | 1.1654 us | 17.91 |    0.02 |     36.1328 |           - |           - |           113.56 KB |
 NetSerializer | 448.12 us | 0.0985 us | 0.0873 us |  8.35 |    0.01 |     32.7148 |           - |           - |           101.81 KB |
   MessagePack | 376.43 us | 0.1226 us | 0.1147 us |  7.02 |    0.00 |     32.7148 |           - |           - |           101.63 KB |
      Hyperion | 264.44 us | 0.1129 us | 0.0942 us |  4.93 |    0.00 |     48.3398 |           - |           - |           149.46 KB |
          Apex |  53.64 us | 0.0324 us | 0.0303 us |  1.00 |    0.00 |     33.0811 |           - |           - |           101.81 KB |
```

#### Immutable Poco Serialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 632.24 us | 4.4961 us | 4.2057 us | 39.42 |    0.27 |     12.6953 |           - |           - |             40251 B |
 NetSerializer | 633.22 us | 0.1715 us | 0.1520 us | 39.47 |    0.02 |           - |           - |           - |                   - |
   MessagePack | 577.94 us | 0.2050 us | 0.1918 us | 36.03 |    0.02 |           - |           - |           - |                   - |
      Hyperion | 191.32 us | 0.0296 us | 0.0277 us | 11.93 |    0.01 |     12.6953 |           - |           - |             40304 B |
          Apex |  16.04 us | 0.0071 us | 0.0059 us |  1.00 |    0.00 |           - |           - |           - |                   - |
```

#### Immutable Poco Deserialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 883.91 us | 2.0485 us | 1.9162 us | 10.71 |    0.02 |     36.1328 |           - |           - |           113.56 KB |
      Hyperion | 728.12 us | 4.1173 us | 3.8514 us |  8.82 |    0.05 |     73.2422 |           - |           - |           227.61 KB |
 NetSerializer | 441.19 us | 0.0556 us | 0.0493 us |  5.35 |    0.00 |     32.7148 |           - |           - |           101.81 KB |
   MessagePack | 380.30 us | 0.1433 us | 0.1271 us |  4.61 |    0.00 |     32.7148 |           - |           - |           101.63 KB |
          Apex |  82.53 us | 0.0670 us | 0.0627 us |  1.00 |    0.00 |     33.0811 |           - |           - |           101.81 KB |
```
