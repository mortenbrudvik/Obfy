using System.Diagnostics;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Regression pins for the Inno Setup script and its build script: PATH edits must keep the system Path a
/// REG_EXPAND_SZ and only touch whole entries, the CLI payload must not carry the original PDBs, and
/// -SkipPublish must not delete the publish output it is supposed to reuse.
/// </summary>
public class InstallerPackagingTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string IssPath = Path.Combine(RepoRoot, "build", "ObfySetup.iss");
    private static readonly string InstallerScriptPath = Path.Combine(RepoRoot, "build", "build-installer.ps1");

    [Fact]
    public void Iss_DeclaresChangesEnvironment_SoThePathEditReachesRunningShells()
    {
        var setup = SetupSection(File.ReadAllText(IssPath));

        setup.ShouldContain("ChangesEnvironment=yes");
    }

    [Fact]
    public void Iss_WritesTheSystemPathAsAnExpandString()
    {
        var code = CodeSection(File.ReadAllText(IssPath));

        // RegWriteStringValue would convert the REG_EXPAND_SZ system Path to REG_SZ, so %SystemRoot%
        // entries stop expanding for every program on the machine.
        code.ShouldNotContain("RegWriteStringValue");
        code.ShouldContain("RegWriteExpandStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path'");
    }

    [Fact]
    public void Iss_EnvRemovePath_MatchesWholeDelimitedEntriesOnly()
    {
        var code = CodeSection(File.ReadAllText(IssPath));
        var removeStart = code.IndexOf("function PathWithoutEntry", StringComparison.Ordinal);
        removeStart.ShouldBeGreaterThanOrEqualTo(0);
        var remove = code[removeStart..code.IndexOf("procedure CurStepChanged", StringComparison.Ordinal)];

        // A substring or prefix match deletes the wrong characters when another entry starts with the
        // install directory (for example "...\Obfy-Beta" listed before "...\Obfy").
        remove.ShouldNotContain("Pos(Uppercase(Path)");
        remove.ShouldNotContain("Pos(';' + Uppercase(Path)");
        remove.ShouldContain("CompareText(");
        remove.ShouldContain("Pos(';'");
    }

    [Fact]
    public void Iss_ExcludesSymbolsFromTheCliPayload()
    {
        var iss = File.ReadAllText(IssPath);
        var cliLine = iss.Split('\n').Single(l => l.TrimStart().StartsWith("Source: \".\\publish\\*\"", StringComparison.Ordinal));

        // The CLI DLLs are self-obfuscated by build-installer.ps1, but the publish folder still holds the
        // pre-obfuscation PDBs; shipping them hands out exactly the symbols the obfuscation hides.
        cliLine.ShouldContain("Excludes: \"*.pdb\"");
    }

    [Fact]
    public void InstallerScript_ProtectsEachPublishDirectoryAsOneClosedSet()
    {
        var script = File.ReadAllText(InstallerScriptPath);
        var step = script[script.IndexOf("Obfuscating assemblies...", StringComparison.Ordinal)..];
        step = step[..step.IndexOf("# Step 6", StringComparison.Ordinal)];

        // One invocation, with every present product assembly. A foreach that obfuscates and copies
        // each DLL before the next one renames Obfy.Core and leaves obfy.dll on the old type names.
        step.ShouldNotContain("foreach ($asmName in $OurAssemblies)");
        var calls = step.Split("& $TempObfyExe").Length - 1;
        calls.ShouldBe(1);
        step.ShouldContain("& $TempObfyExe @inputs -o $OutputDir -c $SelfObfuscationConfig");
        step.ShouldContain("self-obfuscation.json");
        step.ShouldContain("Assert-ProcessedCli");
        step.ShouldContain("Assert-ProcessedUi");
        step.ShouldNotContain("-l standard --string-encrypt --control-flow --rename --strip-metadata");
        script.ShouldContain("HKLM:\\Software\\Policies\\Microsoft\\Edge\\WebView2\\AdditionalBrowserArguments");
        var callAt = step.IndexOf("& $TempObfyExe @inputs", StringComparison.Ordinal);
        var copyAt = step.IndexOf("Copy-Item -LiteralPath $obfuscated", StringComparison.Ordinal);
        copyAt.ShouldBeGreaterThan(callAt);

        foreach (var name in new[]
        {
            "obfy.dll", "ObfyUI.dll", "Obfy.App.dll", "Obfy.Core.dll",
            "Obfy.Interop.dll", "Obfy.WebView.dll", "Obfy.Common.dll",
            "Settings.Core.dll", "Logging.Core.dll"
        })
        {
            step.ShouldContain("\"" + name + "\"");
        }

        step.ShouldNotContain("\"Obfy.UI.dll\"");

        step.ShouldContain("Protect-ProductAssemblies $PublishDir $ObfuscatedCliDir");
        step.ShouldContain("Protect-ProductAssemblies $PublishDirUI $ObfuscatedUiDir");
    }

    [Fact]
    public void InstallerScript_SkipPublish_KeepsTheExistingPublishDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "obfy-installer-skip-" + Guid.NewGuid().ToString("N"));
        var buildDir = Path.Combine(root, "build");
        try
        {
            Directory.CreateDirectory(buildDir);
            var script = Path.Combine(buildDir, "build-installer.ps1");
            File.Copy(InstallerScriptPath, script);

            var publishDir = Path.Combine(buildDir, "publish");
            var publishUiDir = Path.Combine(buildDir, "publish-ui");
            Directory.CreateDirectory(publishDir);
            Directory.CreateDirectory(publishUiDir);
            File.WriteAllText(Path.Combine(publishDir, "obfy.exe"), "cli");
            File.WriteAllText(Path.Combine(publishUiDir, "ObfyUI.exe"), "ui");

            // -SkipBuild never invokes the compiler, so any existing file satisfies -InnoSetupPath.
            var (code, stdout, stderr) = RunPwsh($"-NoProfile -File \"{script}\" -SkipPublish -SkipBuild -InnoSetupPath \"{script}\"");

            code.ShouldBe(0, stdout + stderr);
            File.Exists(Path.Combine(publishDir, "obfy.exe")).ShouldBeTrue("-SkipPublish deleted the CLI publish output");
            File.Exists(Path.Combine(publishUiDir, "ObfyUI.exe")).ShouldBeTrue("-SkipPublish deleted the UI publish output");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string SetupSection(string iss)
    {
        var start = iss.IndexOf("[Setup]", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = iss.IndexOf("[Languages]", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return iss[start..end];
    }

    private static string CodeSection(string iss)
    {
        var start = iss.IndexOf("[Code]", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        return iss[start..];
    }

    private static (int ExitCode, string Stdout, string Stderr) RunPwsh(string args)
    {
        var start = new ProcessStartInfo("pwsh", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderr);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate Obfy.sln from {AppContext.BaseDirectory}");
    }
}
