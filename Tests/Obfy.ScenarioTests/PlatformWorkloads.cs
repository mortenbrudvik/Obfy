namespace Obfy.ScenarioTests;

internal static class PlatformWorkloads
{
    private static readonly Lazy<string> WorkloadList = new(ReadWorkloadList);
    private static readonly string DotnetRoot = FindDotnetRoot();

    public static bool HasMaui() =>
        WorkloadList.Value.Contains("maui", StringComparison.OrdinalIgnoreCase);

    public static bool HasBlazorWasmSdk()
    {
        var sdkRoot = Path.Combine(DotnetRoot, "sdk");
        if (!Directory.Exists(sdkRoot))
            return false;
        return Directory.EnumerateDirectories(sdkRoot).Any(sdk =>
            Directory.Exists(Path.Combine(sdk, "Sdks", "Microsoft.NET.Sdk.BlazorWebAssembly")));
    }

    public static string CurrentWindowsRid() =>
        Environment.Is64BitProcess ? "win-x64" : "win-x86";

    private static string ReadWorkloadList()
    {
        try
        {
            var result = ScenarioHarness.RunProcess("dotnet", "workload list", Directory.GetCurrentDirectory(), 20_000);
            return result.StdOut + result.StdErr;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FindDotnetRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;

        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
        {
            var dir = Path.GetDirectoryName(exe);
            if (dir != null && File.Exists(Path.Combine(dir, "dotnet.exe")))
                return dir;
        }

        return @"C:\Program Files\dotnet";
    }
}
