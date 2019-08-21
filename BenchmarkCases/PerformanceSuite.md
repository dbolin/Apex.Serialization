
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
|  STree_ISD_IS_FullHistory | 173.75 us | 0.8236 us | 0.7704 us |       - |      - |     - |         - |
|  DTree_ISD_IS_FullHistory | 885.52 us | 5.7649 us | 5.3925 us | 11.7188 | 2.9297 |     - |  654240 B |
| SGraph_ISD_IS_FullHistory |  46.70 us | 0.4185 us | 0.3710 us |       - |      - |     - |         - |
| DGraph_ISD_IS_FullHistory |  93.85 us | 1.2763 us | 1.1314 us |  5.3711 |      - |     - |   59368 B |

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

|         Method |     Mean |     Error |    StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|--------------- |---------:|----------:|----------:|-------:|------:|------:|----------:|
| S_NullableInts | 3.582 us | 0.0100 us | 0.0093 us |      - |     - |     - |         - |
| D_NullableInts | 5.318 us | 0.1031 us | 0.1227 us | 0.6638 |     - |     - |    8248 B |

|            Method |      Mean |     Error |    StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------ |----------:|----------:|----------:|-------:|------:|------:|----------:|
| S_NullableWrapper |  3.540 us | 0.0141 us | 0.0125 us |      - |     - |     - |         - |
| D_NullableWrapper | 10.697 us | 0.0948 us | 0.0886 us | 3.9978 |     - |     - |   49208 B |
