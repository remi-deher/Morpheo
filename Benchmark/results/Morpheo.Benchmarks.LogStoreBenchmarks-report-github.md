```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7462/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i5-1345U 1.60GHz, 1 CPU, 12 logical and 10 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3


```
| Method              | N    | Mean      | Error     | StdDev    | Ratio | Gen0      | Gen1     | Allocated   | Alloc Ratio |
|-------------------- |----- |----------:|----------:|----------:|------:|----------:|---------:|------------:|------------:|
| **Write_SQLite_EF**     | **100**  |  **6.167 ms** | **0.0730 ms** | **0.0647 ms** |  **1.00** |  **187.5000** |  **62.5000** |  **1298.95 KB** |        **1.00** |
| Write_FileStore_LSM | 100  |  1.083 ms | 0.0181 ms | 0.0169 ms |  0.18 |   19.5313 |        - |    138.9 KB |        0.11 |
|                     |      |           |           |           |       |           |          |             |             |
| **Write_SQLite_EF**     | **1000** | **63.596 ms** | **0.5386 ms** | **0.5038 ms** |  **1.00** | **2000.0000** | **625.0000** | **12970.81 KB** |        **1.00** |
| Write_FileStore_LSM | 1000 | 10.216 ms | 0.0386 ms | 0.0323 ms |  0.16 |  218.7500 |        - |  1391.44 KB |        0.11 |
