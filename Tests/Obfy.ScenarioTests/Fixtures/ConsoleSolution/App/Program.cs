using ConsoleLib;

namespace ConsoleApp;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"SMOKE:{Calculator.Add(2, 3)}");
            return 0;
        }

        Console.WriteLine(Calculator.Add(1, 1));
        return 0;
    }
}
