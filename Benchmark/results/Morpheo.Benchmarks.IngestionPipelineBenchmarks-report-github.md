```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7462/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i5-1345U 1.60GHz, 1 CPU, 12 logical and 10 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3


```
| Method                | Mean         | Error        | StdDev       | Median      | Ratio | RatioSD | Gen0      | Gen1    | Gen2    | Allocated   | Alloc Ratio |
|---------------------- |-------------:|-------------:|-------------:|------------:|------:|--------:|----------:|--------:|--------:|------------:|------------:|
| Pipeline_Standard_SQL | 10,788.50 μs | 1,726.858 μs | 5,091.679 μs | 8,871.33 μs | 1.232 |    0.86 | 2566.4063 | 39.0625 | 15.6250 | 15892.88 KB |       1.000 |
| Pipeline_Morpheo_LSM  |     18.12 μs |     0.432 μs |     1.246 μs |    17.92 μs | 0.002 |    0.00 |    0.3662 |       - |       - |     2.94 KB |       0.000 |
