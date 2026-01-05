using BenchmarkDotNet.Running;

namespace Morpheo.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Running Morpheo Benchmarks...");
        
        // This allows running specific benchmarks via args or all by default.
        // BenchmarkSwitcher is better for multiple benchmark classes.
        var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
