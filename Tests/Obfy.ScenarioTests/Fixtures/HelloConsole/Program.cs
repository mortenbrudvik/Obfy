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

    // Anti-dump overwrites the start of dbghelp!MiniDumpWriteDump with:
    //   x64: 31 C0 C3        (xor eax, eax; ret)
    //   x86: 31 C0 C2 1C 00  (xor eax, eax; ret 0x1C)
    private static bool DumpHookPatched()
    {
        var module = LoadLibraryW("dbghelp.dll");
        var procedure = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "MiniDumpWriteDump");
        if (procedure == IntPtr.Zero)
            return false;

        if (IntPtr.Size == 4)
        {
            return Marshal.ReadByte(procedure, 0) == 0x31
                && Marshal.ReadByte(procedure, 1) == 0xC0
                && Marshal.ReadByte(procedure, 2) == 0xC2
                && Marshal.ReadByte(procedure, 3) == 0x1C
                && Marshal.ReadByte(procedure, 4) == 0x00;
        }

        return Marshal.ReadByte(procedure, 0) == 0x31
            && Marshal.ReadByte(procedure, 1) == 0xC0
            && Marshal.ReadByte(procedure, 2) == 0xC3;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW")]
    private static extern IntPtr LoadLibraryW(string name);

    [DllImport("kernel32", CharSet = CharSet.Ansi, EntryPoint = "GetProcAddress")]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
