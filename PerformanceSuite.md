
Internal benchmarks

|              Method |     Mean |     Error |    StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------------- |---------:|----------:|----------:|-------:|------:|------:|----------:|
| S_SingleEmptyObject | 38.29 ns | 0.2806 ns | 0.2625 ns |      - |     - |     - |         - |
| D_SingleEmptyObject | 57.68 ns | 1.1845 ns | 1.6606 ns | 0.0020 |     - |     - |      24 B |

|          Method |     Mean |     Error |    StdDev |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|---------------- |---------:|----------:|----------:|-------:|-------:|------:|----------:|
| S_ListEmptyFull | 2.009 us | 0.0095 us | 0.0089 us |      - |      - |     - |         - |
| D_ListEmptyFull | 8.940 us | 0.1822 us | 0.4768 us | 2.7313 | 0.2289 |     - |   32824 B |

|            Method |     Mean |     Error |    StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------ |---------:|----------:|----------:|-------:|------:|------:|----------:|
| S_NestedListEmpty | 1.799 us | 0.0047 us | 0.0044 us |      - |     - |     - |         - |
| D_NestedListEmpty | 6.923 us | 0.0962 us | 0.0900 us | 2.5406 |     - |     - |   26696 B |

|             Method |     Mean |     Error |    StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------- |---------:|----------:|----------:|-------:|------:|------:|----------:|
| S_NestedListFields | 20.95 us | 0.1660 us | 0.1553 us |      - |     - |     - |         - |
| D_NestedListFields | 58.26 us | 1.2793 us | 1.6179 us | 7.7515 |     - |     - |   87968 B |

|               Method |     Mean |      Error |     StdDev |   Gen 0 |  Gen 1 | Gen 2 | Allocated |
|--------------------- |---------:|-----------:|-----------:|--------:|-------:|------:|----------:|
| S_ISD_IS_FullHistory | 176.9 us |  0.5762 us |  0.5390 us |       - |      - |     - |         - |
| D_ISD_IS_FullHistory | 939.5 us | 12.4692 us | 11.6637 us | 11.7188 | 1.9531 |     - |  654240 B |


