using System.Diagnostics;
using System.IO;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Shouldly;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// TR-42: launch with a real assembly on the command line (avoids the native file picker)
/// and click Obfuscate. Category=UI, not default CI.
/// </summary>
[Trait("Category", "UI")]
public class ObfuscateFlowTests : IDisposable
{
    private Application? _app;
    private UIA3Automation? _automation;
    private string? _workDir;

    [Fact]
    public void CommandLineAssembly_Obfuscate_WritesOutput()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "obfy-ui-obf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        var dll = BuildTinyLibrary(_workDir);
        var outputDir = Path.Combine(_workDir, "out");
        Directory.CreateDirectory(outputDir);

        var exe = UiExecutableLocator.ResolveFromTestContext(AppContext.BaseDirectory);
        _automation = new UIA3Automation();
        _app = Application.Launch(exe, $"\"{dll}\" -o \"{outputDir}\"");
        var window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("Obfy main window did not appear.");

        WaitUntil(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId("ObfuscateButton"))?.AsButton()?.IsEnabled == true,
            TimeSpan.FromSeconds(10),
            "ObfuscateButton never enabled after command-line file load");

        var obfuscate = window.FindFirstDescendant(cf => cf.ByAutomationId("ObfuscateButton"))!.AsButton();
        obfuscate.Click();

        var expected = Path.Combine(outputDir, Path.GetFileName(dll));
        WaitUntil(() => File.Exists(expected), TimeSpan.FromSeconds(60), $"obfuscated output not written: {expected}");
        new FileInfo(expected).Length.ShouldBeGreaterThan(0);
    }

    public void Dispose()
    {
        try { _app?.Close(); } catch { /* ignore */ }
        _automation?.Dispose();
        if (_workDir != null)
        {
            try { Directory.Delete(_workDir, recursive: true); } catch { /* ignore */ }
        }
        GC.SuppressFinalize(this);
    }

    private static string BuildTinyLibrary(string workDir)
    {
        var src = Path.Combine(workDir, "Lib.cs");
        File.WriteAllText(src, "public static class Lib { public static int N() => 1; }\n");
        var csproj = Path.Combine(workDir, "Lib.csproj");
        File.WriteAllText(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
            </Project>
            """);

        var start = new ProcessStartInfo("dotnet", $"build \"{csproj}\" -c Release --nologo")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet build failed to start");
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException("Timed out building FlaUI dummy library");
        }

        process.ExitCode.ShouldBe(0, process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
        var dll = Path.Combine(workDir, "bin", "Release", "net10.0", "Lib.dll");
        File.Exists(dll).ShouldBeTrue(dll);
        return dll;
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string message)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            Thread.Sleep(200);
        }

        throw new TimeoutException(message);
    }
}
