```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7462/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i5-1345U 1.60GHz, 1 CPU, 12 logical and 10 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3


```
| Method            | N     | Mean     | Error     | StdDev    | Gen0      | Gen1      | Gen2    | Allocated |
|------------------ |------ |---------:|----------:|----------:|----------:|----------:|--------:|----------:|
| **CalculateRootHash** | **1000**  | **1.295 ms** | **0.0069 ms** | **0.0065 ms** |  **177.7344** |   **42.9688** |       **-** |   **1.07 MB** |
| **CalculateRootHash** | **10000** | **8.659 ms** | **0.1684 ms** | **0.1872 ms** | **1828.1250** | **1031.2500** | **46.8750** |  **10.75 MB** |
