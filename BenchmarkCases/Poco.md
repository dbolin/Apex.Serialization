## Pocos

Protobuf-net 2.4.0 / Hyperion 0.9.8 / MessagePack 1.7.3.4 / NetSerializer 4.1.0 / Apex

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
BenchmarkDotNet=v0.11.3, OS=Windows 10.0.17134.407 (1803/April2018Update/Redstone4)
Intel Core i5-4690 CPU 3.50GHz (Haswell), 1 CPU, 4 logical and 4 physical cores
Frequency=3417967 Hz, Resolution=292.5716 ns, Timer=TSC
.NET Core SDK=2.1.500
  [Host] : .NET Core 2.1.6 (CoreCLR 4.6.27019.06, CoreFX 4.6.27019.05), 64bit RyuJIT
```

#### Mutable Poco Serialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 512.91 us | 9.6213 us | 9.8804 us | 17.21 |    0.36 |     12.6953 |           - |           - |             40251 B |
 NetSerializer | 440.37 us | 2.9253 us | 2.4428 us | 14.80 |    0.10 |           - |           - |           - |                   - |
   MessagePack | 388.87 us | 0.6023 us | 0.5634 us | 13.06 |    0.04 |           - |           - |           - |                   - |
      Hyperion | 170.03 us | 0.6921 us | 0.6474 us |  5.71 |    0.03 |     12.6953 |           - |           - |             40304 B |
          Apex |  29.77 us | 0.0805 us | 0.0714 us |  1.00 |    0.00 |           - |           - |           - |                   - |
```

#### Mutable Poco Deserialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 710.43 us | 3.8831 us | 3.4422 us | 14.86 |    0.24 |     36.1328 |           - |           - |           113.56 KB |
 NetSerializer | 475.23 us | 0.8711 us | 0.8148 us |  9.93 |    0.15 |     32.7148 |           - |           - |           101.81 KB |
   MessagePack | 331.52 us | 0.6860 us | 0.5729 us |  6.93 |    0.10 |     32.7148 |           - |           - |           101.63 KB |
      Hyperion | 231.07 us | 1.3997 us | 1.3092 us |  4.83 |    0.07 |     48.5840 |           - |           - |           149.46 KB |
          Apex |  47.88 us | 0.7959 us | 0.7445 us |  1.00 |    0.00 |     33.0811 |           - |           - |           101.81 KB |
```

#### Immutable Poco Serialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 504.51 us | 4.7686 us | 4.4606 us | 34.79 |    0.32 |     12.6953 |           - |           - |             40251 B |
 NetSerializer | 438.32 us | 0.7005 us | 0.6553 us | 30.22 |    0.10 |           - |           - |           - |                   - |
   MessagePack | 388.48 us | 1.1708 us | 1.0379 us | 26.79 |    0.12 |           - |           - |           - |                   - |
      Hyperion | 167.23 us | 1.0379 us | 0.9708 us | 11.53 |    0.08 |     12.6953 |           - |           - |             40304 B |
          Apex |  14.50 us | 0.0431 us | 0.0403 us |  1.00 |    0.00 |           - |           - |           - |                   - |
```

#### Immutable Poco Deserialization
```
        Method |      Mean |     Error |    StdDev | Ratio | RatioSD | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
-------------- |----------:|----------:|----------:|------:|--------:|------------:|------------:|------------:|--------------------:|
      Protobuf | 710.89 us | 2.7400 us | 2.2881 us | 10.42 |    0.04 |     36.1328 |           - |           - |           113.56 KB |
      Hyperion | 587.22 us | 1.3634 us | 1.2753 us |  8.60 |    0.02 |     73.2422 |           - |           - |           227.61 KB |
 NetSerializer | 491.25 us | 0.7875 us | 0.7366 us |  7.20 |    0.02 |     32.2266 |           - |           - |           101.81 KB |
   MessagePack | 339.16 us | 1.1159 us | 0.9318 us |  4.97 |    0.01 |     32.7148 |           - |           - |           101.63 KB |
          Apex |  68.26 us | 0.1153 us | 0.1078 us |  1.00 |    0.00 |     33.0811 |           - |           - |           101.81 KB |
```
