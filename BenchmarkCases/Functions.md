## Function serialization

Hyperion / Apex

Serializing/deserializing delegates

```csharp
        public static void F1()
        { }

        public static void F1(int a)
        { }

        public static void F1(int a, string b, Func<Dictionary<string, object>, Dictionary<string, string>> f)
        { }

        private Action _t1 = F1;
        private Action<int> _t2 = F1;
        private Action<int, string, Func<Dictionary<string, object>, Dictionary<string, string>>> _t3 = F1;
```

#### Hyperion 0.9.8
```
                Method |      Mean |     Error |    StdDev | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
---------------------- |----------:|----------:|----------:|------------:|------------:|------------:|--------------------:|
        FunctionNoArgs |  1.452 us | 0.0072 us | 0.0067 us |      0.3529 |           - |           - |              1112 B |
        FunctionOneArg |  2.159 us | 0.0257 us | 0.0241 us |      0.4959 |           - |           - |              1568 B |
   FunctionComplexArgs |  5.759 us | 0.0285 us | 0.0252 us |      1.5259 |           - |           - |              4803 B |
      D_FunctionNoArgs |  5.196 us | 0.0837 us | 0.0783 us |      0.3052 |           - |           - |               968 B |
     D_FunctionOneArgs |  9.495 us | 0.0666 us | 0.0623 us |      0.4272 |           - |           - |              1368 B |
 D_FunctionComplexArgs | 34.373 us | 0.1867 us | 0.1747 us |      1.7700 |           - |           - |              5608 B |
```

#### Apex
```
                Method |     Mean |     Error |    StdDev | Gen 0/1k Op | Gen 1/1k Op | Gen 2/1k Op | Allocated Memory/Op |
---------------------- |---------:|----------:|----------:|------------:|------------:|------------:|--------------------:|
        FunctionNoArgs | 255.0 ns | 1.6044 ns | 1.5007 ns |           - |           - |           - |                   - |
        FunctionOneArg | 299.0 ns | 0.8713 ns | 0.8150 ns |           - |           - |           - |                   - |
   FunctionComplexArgs | 438.6 ns | 1.3600 ns | 1.2722 ns |           - |           - |           - |                   - |
      D_FunctionNoArgs | 295.0 ns | 2.3364 ns | 2.1855 ns |      0.0224 |           - |           - |                72 B |
     D_FunctionOneArgs | 341.5 ns | 1.2520 ns | 1.1711 ns |      0.0224 |           - |           - |                72 B |
 D_FunctionComplexArgs | 542.3 ns | 1.1440 ns | 1.0701 ns |      0.0219 |           - |           - |                72 B |
```
