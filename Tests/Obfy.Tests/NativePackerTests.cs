using System.Reflection.PortableExecutable;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Obfy.Core.Models;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class NativePackerTests
{
    [Fact]
    public void Pack_ReplacesManagedPe_WithNativeHostAndOverlay()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-native-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToExe("public static class Program { public static int Main() => 11; }", dir, "PackApp");
            var output = Path.Combine(dir, "PackApp.obf.exe");
            File.Copy(input, output);

            NativePacker.Pack(output, PackingSettings(), input).ShouldBe(output);

            File.Exists(output).ShouldBeTrue();
            File.Exists(ManagedLauncherPacker.LauncherPathFor(output)).ShouldBeFalse();
            File.Exists(NativePacker.RuntimeConfigPathFor(output)).ShouldBeTrue();

            var bytes = File.ReadAllBytes(output);
            bytes.AsSpan().IndexOf(NativePacker.Magic).ShouldBeGreaterThanOrEqualTo(0);

            using (var pe = new PEReader(new MemoryStream(bytes)))
            {
                pe.PEHeaders.CorHeader.ShouldBeNull();
            }

            Should.Throw<BadImageFormatException>(() => ModuleDefMD.Load(bytes));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Pack_LibraryWithoutEntryPoint_LeavesManagedPe()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-native-pack-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly("public static class Lib { public static int Get() => 1; }", dir, "Lib");
            var output = Path.Combine(dir, "Lib.obf.dll");
            File.Copy(input, output);

            var ex = Should.Throw<InvalidOperationException>(() => NativePacker.Pack(output, PackingSettings(), input));
            ex.Message.ShouldContain("entry point");
            ex.Message.ShouldNotContain("Packing failed:");

            using var module = ModuleDefMD.Load(output);
            module.EntryPoint.ShouldBeNull();
            module.Types.ShouldContain(t => t.Name == "Lib");
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Pack_SameInputSettings_ProducesIdenticalOverlay()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-native-pack-ident-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToExe("public static class Program { public static int Main() => 11; }", dir, "IdentApp");
            var managed = File.ReadAllBytes(input);
            var settings = PackingSettings();
            var output1 = Path.Combine(dir, "IdentA.exe");
            var output2 = Path.Combine(dir, "IdentB.exe");
            File.WriteAllBytes(output1, managed);
            File.WriteAllBytes(output2, managed);

            NativePacker.Pack(output1, settings, input);
            NativePacker.Pack(output2, settings, input);

            File.ReadAllBytes(output1).ShouldBe(File.ReadAllBytes(output2));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Pack_DifferentInput_ProducesDifferentCiphertext()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-native-pack-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var inputA = CompileToExe("public static class Program { public static int Main() => 11; }", dir, "DiffA");
            var inputB = CompileToExe("public static class Program { public static int Main() => 12; }", dir, "DiffB");
            var outputA = Path.Combine(dir, "DiffA.obf.exe");
            var outputB = Path.Combine(dir, "DiffB.obf.exe");
            File.Copy(inputA, outputA);
            File.Copy(inputB, outputB);
            var settings = PackingSettings();

            NativePacker.Pack(outputA, settings, inputA);
            NativePacker.Pack(outputB, settings, inputB);

            OverlayAfterMagic(File.ReadAllBytes(outputA))
                .ShouldNotBe(OverlayAfterMagic(File.ReadAllBytes(outputB)));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Pack_CopiesSubsystemFromManagedPe()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-native-pack-gui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToExe(
                "public static class Program { public static int Main() => 11; }",
                dir,
                "GuiApp",
                OutputKind.WindowsApplication);
            var output = Path.Combine(dir, "GuiApp.obf.exe");
            File.Copy(input, output);

            using (var managedPe = new PEReader(new MemoryStream(File.ReadAllBytes(output))))
            {
                managedPe.PEHeaders.PEHeader.ShouldNotBeNull();
                managedPe.PEHeaders.PEHeader!.Subsystem.ShouldBe(Subsystem.WindowsGui);
            }

            NativePacker.Pack(output, PackingSettings(), input);

            using var packedPe = new PEReader(new MemoryStream(File.ReadAllBytes(output)));
            packedPe.PEHeaders.PEHeader.ShouldNotBeNull();
            packedPe.PEHeaders.PEHeader!.Subsystem.ShouldBe(Subsystem.WindowsGui);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static ObfySettings PackingSettings() => new()
    {
        Level = ObfuscationLevel.Custom,
        StringEncryption = { Enabled = false },
        SymbolRenaming = { Enabled = false, PreservePublicApi = true },
        Packing = { Enabled = true }
    };

    private static string CompileToAssembly(string source, string dir, string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static string CompileToExe(
        string source, string dir, string assemblyName, OutputKind outputKind = OutputKind.ConsoleApplication)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(outputKind));

        var path = Path.Combine(dir, assemblyName + ".exe");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static byte[] OverlayAfterMagic(byte[] packed)
    {
        var index = packed.AsSpan().IndexOf(NativePacker.Magic);
        index.ShouldBeGreaterThanOrEqualTo(0);
        return packed[(index + NativePacker.Magic.Length)..];
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* ignore */ }
    }
}
