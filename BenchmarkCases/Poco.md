## Pocos

Protobuf-net / Hyperion / MessagePack / Apex

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

#### Protobuf-net 2.4.0
```
             Method |     Mean |     Error |   StdDev | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
------------------- |---------:|----------:|---------:|------------:|------------:|------------:|--------------------:|
 ImmutablePocoWrite | 560.6 us |  2.797 us | 2.617 us |     12.6953 |           - |           - |            39.31 KB |
  ImmutablePocoRead | 765.6 us |  1.834 us | 1.716 us |     36.1328 |           - |           - |           113.58 KB |
          PocoWrite | 558.3 us |  1.450 us | 1.356 us |     12.6953 |           - |           - |            39.31 KB |
           PocoRead | 747.7 us | 10.624 us | 9.937 us |     36.1328 |           - |           - |           113.58 KB |
```

#### Hyperion 0.9.8
```
             Method |     Mean |     Error |    StdDev | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
------------------- |---------:|----------:|----------:|------------:|------------:|------------:|--------------------:|
 ImmutablePocoWrite | 173.5 us | 0.6393 us | 0.5980 us |     12.6953 |           - |           - |            39.36 KB |
  ImmutablePocoRead | 602.1 us | 2.9127 us | 2.7245 us |     70.3125 |     23.4375 |           - |            227.6 KB |
          PocoWrite | 175.1 us | 0.9040 us | 0.8456 us |     12.6953 |           - |           - |            39.36 KB |
           PocoRead | 604.8 us | 4.2222 us | 3.9494 us |     70.3125 |     23.4375 |           - |            227.6 KB |
```

#### MessagePack 1.7.3.4
```
             Method |     Mean |     Error |    StdDev | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
------------------- |---------:|----------:|----------:|------------:|------------:|------------:|--------------------:|
 ImmutablePocoWrite | 118.9 us | 0.3185 us | 0.2979 us |           - |           - |           - |                   - |
  ImmutablePocoRead | 315.3 us | 1.7914 us | 1.5881 us |     28.8086 |      9.2773 |           - |            104064 B |
          PocoWrite | 121.7 us | 1.4499 us | 1.3563 us |           - |           - |           - |                   - |
           PocoRead | 317.0 us | 4.6244 us | 4.0994 us |     28.8086 |      9.2773 |           - |            104064 B |
```

#### Apex
```
             Method |     Mean |     Error |    StdDev | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
------------------- |---------:|----------:|----------:|------------:|------------:|------------:|--------------------:|
 ImmutablePocoWrite | 19.73 us | 0.0446 us | 0.0395 us |           - |           - |           - |                   - |
  ImmutablePocoRead | 93.15 us | 0.3215 us | 0.2850 us |     30.1514 |      6.4697 |           - |            104256 B |
          PocoWrite | 31.45 us | 0.5846 us | 0.5468 us |           - |           - |           - |                   - |
           PocoRead | 54.92 us | 1.0948 us | 1.0241 us |     30.0903 |      6.9580 |           - |            104256 B |
```
