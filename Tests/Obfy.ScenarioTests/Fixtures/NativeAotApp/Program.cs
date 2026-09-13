namespace NativeAotApp;

public static class Program
{
    public static int Main()
    {
        Console.WriteLine($"SMOKE:{Lib.Run()}");
        return 0;
    }
}

public static class Lib
{
    public static int Run()
    {
        var secret = "aot-secret";
        return Helper.Len(secret);
    }
}

internal static class Helper
{
    public static int Len(string s) => s.Length;
}
