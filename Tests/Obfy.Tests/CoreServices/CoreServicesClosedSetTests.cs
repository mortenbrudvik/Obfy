using Autofac;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests.CoreServices;

/// <summary>Closed-set orchestration: settings rules, commit clean-up, no-op warnings, signing references.</summary>
public class CoreServicesClosedSetTests : IDisposable
{
    private readonly string _dir = CoreServicesTestSupport.NewTempDirectory("obfy-closed-svc");

    public void Dispose() => CoreServicesTestSupport.DeleteQuietly(_dir);

    private static ClosedSetInput Input(string path) =>
        new() { AssemblyPath = path, Hints = new ProjectSettingsHints() };

    private (string A, string B) TwoLibs(string bodyA = "internal static string S() => \"closed-set literal A\";")
    {
        var a = CoreServicesTestSupport.CompileLibrary(_dir, "LibA", $"internal class A {{ {bodyA} }}");
        var b = CoreServicesTestSupport.CompileLibrary(_dir, "LibB", "internal class B { internal static string S() => \"closed-set literal B\"; }");
        return (a, b);
    }

    [Fact]
    public async Task NonCustomLevel_AppliesThePreset_LikeASingleFileRun()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var (a, b) = TwoLibs();

        // Minimal turns string encryption off. The default settings bag has it on, so only a run that
        // applies the level reaches the Minimal result.
        var minimal = await service.ObfuscateClosedSetAsync(
            [Input(a), Input(b)], Path.Combine(_dir, "out-min"), new ObfySettings { Level = ObfuscationLevel.Minimal });
        var custom = CoreServicesTestSupport.AllOff();
        custom.StringEncryption.Enabled = true;
        var control = await service.ObfuscateClosedSetAsync(
            [Input(a), Input(b)], Path.Combine(_dir, "out-custom"), custom);

        minimal.Success.ShouldBeTrue(minimal.ErrorMessage);
        control.Success.ShouldBeTrue(control.ErrorMessage);
        control.ModuleResults.Sum(r => r.Statistics.StringsEncrypted).ShouldBeGreaterThan(0);
        minimal.ModuleResults.Sum(r => r.Statistics.StringsEncrypted).ShouldBe(0);
    }

    [Fact]
    public async Task CustomLevel_KeepsExplicitFlags_NotOverwrittenByAPreset()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var (a, b) = TwoLibs();
        var settings = CoreServicesTestSupport.AllOff();
        settings.StringEncryption.Enabled = true;
        settings.Metadata.RemoveDebugInfo = false;

        var result = await service.ObfuscateClosedSetAsync([Input(a), Input(b)], Path.Combine(_dir, "out"), settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.ModuleResults.Sum(r => r.Statistics.StringsEncrypted).ShouldBeGreaterThan(0);
        result.ModuleResults.Sum(r => r.Statistics.TypesRenamed).ShouldBe(0);
    }

    [Fact]
    public async Task InvalidSettings_ReturnAFailedResult_NothingIsWritten()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var (a, b) = TwoLibs();
        var outDir = Path.Combine(_dir, "out");
        var settings = CoreServicesTestSupport.AllOff();
        settings.StringEncryption.MinStringLength = 0; // [Range(1, 100)]

        var result = await service.ObfuscateClosedSetAsync([Input(a), Input(b)], outDir, settings);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain("Invalid settings");
        Directory.Exists(outDir).ShouldBeFalse();
    }

    [Fact]
    public async Task SigningEnabledWithoutKeyFile_FailsValidation_LikeASingleFileRun()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var (a, b) = TwoLibs();
        var settings = CoreServicesTestSupport.AllOff();
        settings.Signing.Enabled = true;

        var result = await service.ObfuscateClosedSetAsync([Input(a), Input(b)], Path.Combine(_dir, "out"), settings);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain("Invalid settings");
    }

    [Fact]
    public async Task FailedCommitThatRestoresEverything_LeavesNoObfyprevBackupsBehind()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var (a, b) = TwoLibs();
        var outDir = Path.Combine(_dir, "out");
        Directory.CreateDirectory(outDir);
        var existing = Path.Combine(outDir, "LibA.dll");
        byte[] earlier = [1, 2, 3, 4, 5];
        await File.WriteAllBytesAsync(existing, earlier);
        Directory.CreateDirectory(Path.Combine(outDir, "LibB.dll")); // LibA is placed, then LibB cannot be

        var result = await service.ObfuscateClosedSetAsync([Input(a), Input(b)], outDir, CoreServicesTestSupport.AllOff());

        result.Success.ShouldBeFalse();
        (await File.ReadAllBytesAsync(existing)).ShouldBe(earlier, "the earlier output is restored");
        Directory.GetFiles(outDir, "*.obfyprev", SearchOption.AllDirectories).ShouldBeEmpty();
        Directory.GetFiles(outDir, ".*.obfyprev", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task NoTransformations_EveryModuleResultWarns()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var (a, b) = TwoLibs();

        var result = await service.ObfuscateClosedSetAsync(
            [Input(a), Input(b)], Path.Combine(_dir, "out"), CoreServicesTestSupport.AllOff());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.ModuleResults.Count.ShouldBe(2);
        result.ModuleResults.ShouldAllBe(r => r.Warnings.Any(w => w.Contains("No transformations", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RenamingOnlyRun_IsNotReportedAsNoTransformations()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var (a, b) = TwoLibs();
        var settings = CoreServicesTestSupport.AllOff();
        settings.SymbolRenaming.Enabled = true;
        settings.SymbolRenaming.PreservePublicApi = false;

        var result = await service.ObfuscateClosedSetAsync([Input(a), Input(b)], Path.Combine(_dir, "out"), settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.ModuleResults.Sum(r => r.Statistics.TotalTransformations).ShouldBeGreaterThan(0);
        result.ModuleResults.ShouldAllBe(r => !r.Warnings.Any(w => w.Contains("No transformations", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AlreadyProtectedMember_FailsTheRunBeforeAnythingIsWritten()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var (a, b) = TwoLibs();
        var protectedOutput = Path.Combine(_dir, "LibA.protected.dll");
        var first = CoreServicesTestSupport.AllOff();
        first.StringEncryption.Enabled = true;
        (await service.ObfuscateAsync(a, protectedOutput, first)).Success.ShouldBeTrue();
        var outDir = Path.Combine(_dir, "out");

        var result = await service.ObfuscateClosedSetAsync(
            [Input(protectedOutput), Input(b)], outDir, CoreServicesTestSupport.AllOff());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain("already protected");
        Directory.Exists(outDir).ShouldBeFalse();
    }

    [Fact]
    public async Task WithSigning_AssemblyRefsToOtherMembers_CarryTheNewPublicKeyToken()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();

        // LibB ships signed with an old key and the exe references it by that key's token.
        var oldKeyPath = CoreServicesTestSupport.WriteSnk(Path.Combine(_dir, "old.snk"));
        var newKeyPath = CoreServicesTestSupport.WriteSnk(Path.Combine(_dir, "new.snk"));
        var oldKey = new StrongNameKey(oldKeyPath);
        var newKey = new StrongNameKey(newKeyPath);
        var libB = CoreServicesTestSupport.CompileLibrary(_dir, "LibB", "public class B { public static int N() => 7; }");
        SignWithKey(libB, oldKey);
        var app = CoreServicesTestSupport.CompileExe(
            _dir, "App", "public static class P { public static void Main() { System.Console.WriteLine(B.N()); } }", libB);
        SetReferenceToken(app, "LibB", new PublicKey(oldKey.PublicKey).Token);

        var settings = CoreServicesTestSupport.AllOff();
        settings.Signing.Enabled = true;
        settings.Signing.KeyFile = newKeyPath;
        var outDir = Path.Combine(_dir, "out");

        var result = await service.ObfuscateClosedSetAsync([Input(app), Input(libB)], outDir, settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        var expected = new PublicKey(newKey.PublicKey).Token;
        using var outLib = ModuleDefMD.Load(Path.Combine(outDir, "LibB.dll"));
        outLib.Assembly.PublicKeyToken.ShouldBe(expected, "the lib is re-signed with the new key");
        using var outApp = ModuleDefMD.Load(Path.Combine(outDir, "App.exe"));
        var reference = outApp.GetAssemblyRefs().Single(r => r.Name == "LibB");
        reference.PublicKeyOrToken.ShouldNotBeNull();
        reference.PublicKeyOrToken!.Token.ShouldBe(expected, "the exe must reference the lib by the new token");
    }

    private static void SignWithKey(string path, StrongNameKey key)
    {
        var bytes = File.ReadAllBytes(path);
        using var module = ModuleDefMD.Load(bytes);
        module.Assembly.PublicKey = new PublicKey(key.PublicKey);
        module.Assembly.HasPublicKey = true;
        module.Write(path, new ModuleWriterOptions(module) { StrongNameKey = key });
    }

    private static void SetReferenceToken(string path, string referenceName, PublicKeyToken token)
    {
        var bytes = File.ReadAllBytes(path);
        using var module = ModuleDefMD.Load(bytes);
        module.GetAssemblyRefs().Single(r => r.Name == referenceName).PublicKeyOrToken = token;
        module.Write(path);
    }
}
