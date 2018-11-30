## Case 1

Protobuf-net / Hyperion / MsgPack / Apex

Serializing/deserializing a list of 10000 small poco objects defined as follows

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

#### Protobuf-net
```
             Method |     Mean |     Error |    StdDev | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
------------------- |---------:|----------:|----------:|------------:|------------:|------------:|--------------------:|
 ImmutablePocoWrite | 5.762 ms | 0.0531 ms | 0.0496 ms |    125.0000 |           - |           - |           390.89 KB |
  ImmutablePocoRead | 9.992 ms | 0.1870 ms | 0.1837 ms |    203.1250 |     93.7500 |     31.2500 |             1198 KB |
          PocoWrite | 5.791 ms | 0.0299 ms | 0.0265 ms |    125.0000 |           - |           - |           390.89 KB |
           PocoRead | 9.395 ms | 0.1173 ms | 0.0979 ms |    203.1250 |     93.7500 |     31.2500 |          1197.97 KB |
```

#### Hyperion
```
             Method |     Mean |     Error |    StdDev | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
------------------- |---------:|----------:|----------:|------------:|------------:|------------:|--------------------:|
          PocoWrite | 1.831 ms | 0.0236 ms | 0.0221 ms |    126.9531 |           - |           - |           390.92 KB |
           PocoRead | 9.437 ms | 0.1837 ms | 0.2806 ms |    406.2500 |    156.2500 |     62.5000 |          2366.13 KB |
 ImmutablePocoWrite | 1.862 ms | 0.0254 ms | 0.0238 ms |    126.9531 |           - |           - |           390.92 KB |
  ImmutablePocoRead | 9.601 ms | 0.2089 ms | 0.2145 ms |    421.8750 |    156.2500 |     62.5000 |          2366.15 KB |
```

#### MsgPack
```
             Method |     Mean |     Error |    StdDev | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
------------------- |---------:|----------:|----------:|------------:|------------:|------------:|--------------------:|
 ImmutablePocoWrite | 4.145 ms | 0.0635 ms | 0.0594 ms |    492.1875 |    492.1875 |    492.1875 |          1920.09 KB |
  ImmutablePocoRead | 6.166 ms | 0.0352 ms | 0.0330 ms |    164.0625 |     78.1250 |           - |          1015.69 KB |
          PocoWrite | 4.081 ms | 0.0545 ms | 0.0510 ms |    492.1875 |    492.1875 |    492.1875 |          1920.09 KB |
           PocoRead | 6.086 ms | 0.0448 ms | 0.0419 ms |    164.0625 |     78.1250 |           - |          1015.69 KB |
```

#### Apex
```
             Method |       Mean |     Error |    StdDev | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
------------------- |-----------:|----------:|----------:|------------:|------------:|------------:|--------------------:|
 ImmutablePocoWrite |   237.1 us |  1.354 us |  1.266 us |           - |           - |           - |                   - |
  ImmutablePocoRead | 2,826.4 us | 54.443 us | 50.926 us |    183.5938 |     97.6563 |     31.2500 |           1091137 B |
          PocoWrite |   366.9 us |  2.391 us |  2.237 us |           - |           - |           - |                   - |
           PocoRead | 2,127.3 us | 27.022 us | 25.276 us |    175.7813 |     89.8438 |     31.2500 |           1091136 B |
```
