
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

|                    Method |      Mean |     Error |    StdDev |   Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------------------- |----------:|----------:|----------:|--------:|-------:|------:|----------:|
|  STree_ISD_IS_FullHistory | 172.63 us | 0.6290 us | 0.5576 us |       - |      - |     - |         - |
|  DTree_ISD_IS_FullHistory | 923.33 us | 3.8773 us | 3.4371 us | 11.7188 | 2.9297 |     - |  654240 B |
| SGraph_ISD_IS_FullHistory |  46.63 us | 0.4250 us | 0.3976 us |       - |      - |     - |         - |
| DGraph_ISD_IS_FullHistory |  93.33 us | 0.4612 us | 0.3851 us |  5.6152 | 0.8545 |     - |   59368 B |

|               Method |     Mean |    Error |    StdDev |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|--------------------- |---------:|---------:|----------:|-------:|-------:|------:|----------:|
| S_DictionaryOfValues | 225.3 ns | 8.765 ns | 10.434 ns |      - |      - |     - |         - |
| D_DictionaryOfValues | 750.8 ns | 9.825 ns |  7.671 ns | 0.3881 | 0.0038 |     - |    4064 B |

|                   Method |     Mean |     Error |    StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------------- |---------:|----------:|----------:|-------:|------:|------:|----------:|
| S_DictionaryOfReferences | 3.232 us | 0.0087 us | 0.0077 us |      - |     - |     - |         - |
| D_DictionaryOfReferences | 8.015 us | 0.1418 us | 0.1893 us | 0.7477 |     - |     - |    7920 B |

|                     Method |     Mean |     Error |    StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|--------------------------- |---------:|----------:|----------:|-------:|------:|------:|----------:|
| S_SortedDictionaryOfValues | 1.351 us | 0.0048 us | 0.0045 us |      - |     - |     - |         - |
| D_SortedDictionaryOfValues | 9.416 us | 0.1115 us | 0.0989 us | 0.4425 |     - |     - |    4936 B |

