using System;
using System.Runtime.InteropServices;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        Console.WriteLine("Sum:" + Sum(10));
        if (args.Length > 0 && args[0] == "--probe-dump")
            Console.WriteLine("dump-hook:" + (DumpHookPatched() ? "patched" : "intact"));
        return 0;
    }

    private static int Sum(int n)
    {
        var total = 0;
        for (var i = 1; i <= n; i++)
            total += i % 3 == 0 ? i * 7 : i;
        return total;
    }

    // Anti-dump overwrites the first byte of dbghelp!MiniDumpWriteDump with a ret (0xC3).
    private static bool DumpHookPatched()
    {
        var module = LoadLibraryW("dbghelp.dll");
        var procedure = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "MiniDumpWriteDump");
        return procedure != IntPtr.Zero && Marshal.ReadByte(procedure) == 0xC3;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW")]
    private static extern IntPtr LoadLibraryW(string name);

    [DllImport("kernel32", CharSet = CharSet.Ansi, EntryPoint = "GetProcAddress")]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
