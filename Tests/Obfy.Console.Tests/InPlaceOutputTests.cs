using System.Text;
using dnlib.DotNet;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// <c>obfy app.dll -o &lt;app's own directory&gt;</c> (the documented MSBuild AfterBuild integration) writes the
/// output over the input. It must go through a temp file and replace, so the result is complete and the
/// directory is left clean.
/// </summary>
public class InPlaceOutputTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"ObfyInPlace_{Guid.NewGuid():N}");

    public InPlaceOutputTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Obfuscate_OutputDirectoryIsTheInputDirectory_ReplacesTheInputInPlace()
    {
        var input = ConsoleTestAssembly.Create(_tempDirectory, "InPlaceApp.dll");
        var original = File.ReadAllBytes(input);
        original.AsSpan().IndexOf(Encoding.Unicode.GetBytes("Hello, World!")).ShouldBeGreaterThanOrEqualTo(0);

        var exitCode = await Program.Main([input, "-o", _tempDirectory, "--no-logo"]);

        exitCode.ShouldBe(0);
        var bytes = File.ReadAllBytes(input);
        bytes.ShouldNotBe(original);
        bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("Hello, World!")).ShouldBe(-1,
            "the plaintext literal must not survive in the replaced assembly");
        using (var module = ModuleDefMD.Load(bytes))
            module.Types.Count.ShouldBeGreaterThan(0);

        Directory.GetFiles(_tempDirectory).Select(Path.GetFileName)
            .Where(n => !string.Equals(n, "InPlaceApp.dll", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty("no temp or backup files may be left beside the replaced assembly");
    }
}
