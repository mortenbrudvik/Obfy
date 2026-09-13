namespace ConsoleLib;

public static class Calculator
{
    public static int Add(int a, int b) => a + b;

    internal static string Secret() => "lib-internal-secret";
}
