```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7462/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i5-1345U 1.60GHz, 1 CPU, 12 logical and 10 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  Job-CNUJVU : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3


```
| Method    | Job        | InvocationCount | UnrollFactor | NodeCount | Mean        | Error       | StdDev      | Median      | Gen0    | Gen1   | Allocated |
|---------- |----------- |---------------- |------------- |---------- |------------:|------------:|------------:|------------:|--------:|-------:|----------:|
| **CompareTo** | **DefaultJob** | **Default**         | **16**           | **10**        |    **300.9 ns** |     **6.02 ns** |    **13.48 ns** |    **306.6 ns** |  **0.1273** |      **-** |     **800 B** |
| Merge     | Job-CNUJVU | 1               | 1            | 10        |  2,475.0 ns |    96.86 ns |   279.47 ns |  2,400.0 ns |       - |      - |      56 B |
| **CompareTo** | **DefaultJob** | **Default**         | **16**           | **100**       |  **2,761.5 ns** |    **16.60 ns** |    **13.86 ns** |  **2,760.6 ns** |  **1.1787** | **0.0191** |    **7416 B** |
| Merge     | Job-CNUJVU | 1               | 1            | 100       | 11,759.8 ns |   298.05 ns |   815.90 ns | 11,900.0 ns |       - |      - |      56 B |
| **CompareTo** | **DefaultJob** | **Default**         | **16**           | **1000**      | **30,296.5 ns** |   **462.27 ns** |   **409.79 ns** | **30,272.2 ns** | **11.6272** | **1.1597** |   **73192 B** |
| Merge     | Job-CNUJVU | 1               | 1            | 1000      | 97,070.0 ns | 1,744.26 ns | 2,008.69 ns | 97,300.0 ns |       - |      - |      56 B |
