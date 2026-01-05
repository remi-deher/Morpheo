```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7462/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i5-1345U 1.60GHz, 1 CPU, 12 logical and 10 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3


```
| Method      | Mean     | Error     | StdDev    | Gen0     | Gen1     | Gen2     | Allocated |
|------------ |---------:|----------:|----------:|---------:|---------:|---------:|----------:|
| ComputeDiff | 1.505 ms | 0.0299 ms | 0.0669 ms | 562.5000 | 484.3750 | 453.1250 |   2.72 MB |
| ApplyPatch  | 1.152 ms | 0.0226 ms | 0.0301 ms | 531.2500 | 523.4375 | 523.4375 |   2.07 MB |
