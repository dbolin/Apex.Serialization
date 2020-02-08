
Internal benchmarks

|              Method |     Mean |    Error |   StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------------- |---------:|---------:|---------:|-------:|------:|------:|----------:|
| S_SingleEmptyObject | 40.29 ns | 0.263 ns | 0.246 ns |      - |     - |     - |         - |
| D_SingleEmptyObject | 56.77 ns | 0.289 ns | 0.256 ns | 0.0015 |     - |     - |      24 B |

|          Method |     Mean |     Error |    StdDev |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|---------------- |---------:|----------:|----------:|-------:|-------:|------:|----------:|
| S_ListEmptyFull | 1.791 us | 0.0059 us | 0.0055 us |      - |      - |     - |         - |
| D_ListEmptyFull | 7.787 us | 0.1065 us | 0.0996 us | 2.2278 | 0.1678 |     - |   32824 B |

|            Method |     Mean |     Error |    StdDev |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|------------------ |---------:|----------:|----------:|-------:|-------:|------:|----------:|
| S_NestedListEmpty | 1.747 us | 0.0035 us | 0.0033 us |      - |      - |     - |         - |
| D_NestedListEmpty | 6.295 us | 0.0380 us | 0.0317 us | 1.7471 | 0.0076 |     - |   26696 B |

|             Method |     Mean |    Error |   StdDev |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|------------------- |---------:|---------:|---------:|-------:|-------:|------:|----------:|
| S_NestedListFields | 14.87 us | 0.036 us | 0.034 us |      - |      - |     - |         - |
| D_NestedListFields | 25.90 us | 0.207 us | 0.193 us | 5.7678 | 1.2512 |     - |   87968 B |

|                    Method |      Mean |     Error |    StdDev |   Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------------------- |----------:|----------:|----------:|--------:|-------:|------:|----------:|
|  STree_ISD_IS_FullHistory | 161.34 us |  0.337 us |  0.281 us |       - |      - |     - |         - |
|  DTree_ISD_IS_FullHistory | 918.26 us | 18.170 us | 22.314 us | 12.6953 | 4.8828 |     - |  654240 B |
| SGraph_ISD_IS_FullHistory |  46.35 us |  0.207 us |  0.193 us |       - |      - |     - |         - |
| DGraph_ISD_IS_FullHistory |  90.63 us |  0.987 us |  0.875 us |  3.9063 |      - |     - |   59368 B |

|               Method |     Mean |    Error |   StdDev |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|--------------------- |---------:|---------:|---------:|-------:|-------:|------:|----------:|
| S_DictionaryOfValues | 243.6 ns |  0.48 ns |  0.44 ns |      - |      - |     - |         - |
| D_DictionaryOfValues | 701.5 ns | 12.09 ns | 10.72 ns | 0.2670 | 0.0019 |     - |    4064 B |

|                   Method |     Mean |     Error |    StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------------- |---------:|----------:|----------:|-------:|------:|------:|----------:|
| S_DictionaryOfReferences | 3.200 us | 0.0076 us | 0.0071 us |      - |     - |     - |         - |
| D_DictionaryOfReferences | 7.971 us | 0.0777 us | 0.0727 us | 0.5188 |     - |     - |    7920 B |

|                     Method |     Mean |     Error |    StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|--------------------------- |---------:|----------:|----------:|-------:|------:|------:|----------:|
| S_SortedDictionaryOfValues | 1.224 us | 0.0035 us | 0.0032 us |      - |     - |     - |         - |
| D_SortedDictionaryOfValues | 8.813 us | 0.0507 us | 0.0423 us | 0.3204 |     - |     - |    4936 B |

|         Method |     Mean |     Error |    StdDev |  Gen 0 | Gen 1 | Gen 2 | Allocated |
|--------------- |---------:|----------:|----------:|-------:|------:|------:|----------:|
| S_NullableInts | 3.632 us | 0.0078 us | 0.0073 us |      - |     - |     - |         - |
| D_NullableInts | 5.321 us | 0.0237 us | 0.0222 us | 0.5493 |     - |     - |    8248 B |

|            Method |      Mean |     Error |    StdDev |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|------------------ |----------:|----------:|----------:|-------:|-------:|------:|----------:|
| S_NullableWrapper |  3.613 us | 0.0125 us | 0.0111 us |      - |      - |     - |         - |
| D_NullableWrapper | 10.844 us | 0.1162 us | 0.1030 us | 3.6621 | 0.2747 |     - |   49208 B |

|       Method |      Mean |     Error |    StdDev |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|------------- |----------:|----------:|----------:|-------:|-------:|------:|----------:|
| S_StringList |  6.673 us | 0.1332 us | 0.1368 us |      - |      - |     - |         - |
| D_StringList | 16.332 us | 0.2117 us | 0.1981 us | 2.8076 | 0.0916 |     - |   41016 B |
