using BenchmarkDotNet.Running;

namespace Morpheo.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Running Morpheo Benchmarks...");
        
        var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
