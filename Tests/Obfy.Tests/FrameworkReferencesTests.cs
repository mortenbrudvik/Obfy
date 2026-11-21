using Autofac;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Injected framework references must resolve at run time against the framework the input targets, and the injected
/// code must use only members that framework has. Which assembly carries a type, and which members exist, differ
/// per target: an SDK-built app's core library is the <c>System.Runtime</c> facade (<c>mscorlib</c> on .NET
/// Framework, <c>netstandard</c> for libraries), and that facade does not carry <c>DllNotFoundException</c> or
/// <c>Marshal</c> on any release, <c>RuntimeInformation</c> and the hashing types before .NET 7, <c>File</c> before
/// .NET 6, and much more on .NET Core 3.1. <c>EndToEndObfuscationTests</c> cannot see this: it compiles against the
/// runtime's implementation assemblies, whose core library is <c>System.Private.CoreLib</c>.
/// </summary>
public class FrameworkReferencesTests
{
    private const string PublicKeyToken = "b03f5f7f11d50a3a";

    private static ModuleDefUser ModuleWithCorlib(string corlibName, Version version, string token = PublicKeyToken)
    {
        var corlib = new AssemblyRefUser(corlibName, version, new PublicKeyToken(token), UTF8String.Empty);
        return new ModuleDefUser("Subject", Guid.NewGuid(), corlib);
    }

    private static ModuleDefUser ModuleWithCorlib(string corlibName, string version) =>
        ModuleWithCorlib(corlibName, Version.Parse(version));

    #region Scoping

    [Theory]
    // The facade lacks these on every release
    [InlineData("System.Runtime", "10.0.0.0", "System", "DllNotFoundException", "System.Runtime.InteropServices", "10.0.0.0")]
    [InlineData("System.Runtime", "8.0.0.0", "System.Runtime.InteropServices", "Marshal", "System.Runtime.InteropServices", "8.0.0.0")]
    [InlineData("System.Runtime", "4.2.2.0", "System", "DllNotFoundException", "System.Runtime.InteropServices", "4.2.2.0")]
    // ...and carries these from .NET 5 or 6 on
    [InlineData("System.Runtime", "8.0.0.0", "System", "EntryPointNotFoundException", "System.Runtime", "8.0.0.0")]
    [InlineData("System.Runtime", "8.0.0.0", "System.IO", "File", "System.Runtime", "8.0.0.0")]
    [InlineData("System.Runtime", "6.0.0.0", "System.IO", "File", "System.Runtime", "6.0.0.0")]
    [InlineData("System.Runtime", "5.0.0.0", "System.IO", "File", "System.IO.FileSystem", "5.0.0.0")]
    [InlineData("System.Runtime", "7.0.0.0", "System.Runtime.InteropServices", "RuntimeInformation", "System.Runtime", "7.0.0.0")]
    [InlineData("System.Runtime", "6.0.0.0", "System.Runtime.InteropServices", "RuntimeInformation", "System.Runtime.InteropServices.RuntimeInformation", "6.0.0.0")]
    [InlineData("System.Runtime", "6.0.0.0", "System.Runtime.InteropServices", "Architecture", "System.Runtime.InteropServices.RuntimeInformation", "6.0.0.0")]
    [InlineData("System.Runtime", "7.0.0.0", "System.Security.Cryptography", "SHA256", "System.Security.Cryptography", "7.0.0.0")]
    [InlineData("System.Runtime", "6.0.0.0", "System.Security.Cryptography", "SHA256", "System.Security.Cryptography.Algorithms", "6.0.0.0")]
    [InlineData("System.Runtime", "6.0.0.0", "System.Security.Cryptography", "Aes", "System.Security.Cryptography.Algorithms", "6.0.0.0")]
    [InlineData("System.Runtime", "6.0.0.0", "System.Security.Cryptography", "ICryptoTransform", "System.Security.Cryptography.Primitives", "6.0.0.0")]
    [InlineData("System.Runtime", "5.0.0.0", "System.Security.Cryptography", "Aes", "System.Security.Cryptography.Algorithms", "5.0.0.0")]
    [InlineData("System.Runtime", "10.0.0.0", "System.Security.Cryptography", "ICryptoTransform", "System.Security.Cryptography", "10.0.0.0")]
    // .NET Core 3.x: the contracts have their own versions, which a reference may not exceed
    [InlineData("System.Runtime", "4.2.2.0", "System", "Environment", "System.Runtime.Extensions", "4.2.2.0")]
    [InlineData("System.Runtime", "4.2.2.0", "System", "Convert", "System.Runtime.Extensions", "4.2.2.0")]
    [InlineData("System.Runtime", "4.2.2.0", "System.Diagnostics", "Debugger", "System.Diagnostics.Debug", "4.1.2.0")]
    [InlineData("System.Runtime", "4.2.2.0", "System.IO", "File", "System.IO.FileSystem", "4.1.2.0")]
    [InlineData("System.Runtime", "4.2.2.0", "System.Resources", "ResourceManager", "System.Resources.ResourceManager", "4.1.2.0")]
    [InlineData("System.Runtime", "4.2.2.0", "System.Runtime.InteropServices", "RuntimeInformation", "System.Runtime.InteropServices.RuntimeInformation", "4.0.4.0")]
    [InlineData("System.Runtime", "4.2.2.0", "System.Security.Cryptography", "SHA256", "System.Security.Cryptography.Algorithms", "4.3.2.0")]
    [InlineData("System.Runtime", "4.2.2.0", "System.Security.Cryptography", "ICryptoTransform", "System.Security.Cryptography.Primitives", "4.1.2.0")]
    // A type no table row names stays on the core library
    [InlineData("System.Runtime", "6.0.0.0", "System", "String", "System.Runtime", "6.0.0.0")]
    [InlineData("System.Runtime", "4.2.2.0", "System", "String", "System.Runtime", "4.2.2.0")]
    // .NET Framework carries everything but Aes; netstandard everything
    [InlineData("mscorlib", "4.0.0.0", "System", "DllNotFoundException", "mscorlib", "4.0.0.0")]
    [InlineData("mscorlib", "4.0.0.0", "System.Runtime.InteropServices", "RuntimeInformation", "mscorlib", "4.0.0.0")]
    [InlineData("mscorlib", "4.0.0.0", "System.Security.Cryptography", "SHA256", "mscorlib", "4.0.0.0")]
    [InlineData("mscorlib", "4.0.0.0", "System.Security.Cryptography", "Aes", "System.Core", "4.0.0.0")]
    [InlineData("netstandard", "2.0.0.0", "System", "DllNotFoundException", "netstandard", "2.0.0.0")]
    [InlineData("netstandard", "2.0.0.0", "System.Security.Cryptography", "Aes", "netstandard", "2.0.0.0")]
    // System.Private.CoreLib defines nearly everything; only the hashing types sit outside it
    [InlineData("System.Private.CoreLib", "8.0.0.0", "System", "Environment", "System.Private.CoreLib", "8.0.0.0")]
    [InlineData("System.Private.CoreLib", "8.0.0.0", "System.Runtime.InteropServices", "Marshal", "System.Private.CoreLib", "8.0.0.0")]
    [InlineData("System.Private.CoreLib", "8.0.0.0", "System.Security.Cryptography", "Aes", "System.Security.Cryptography", "8.0.0.0")]
    [InlineData("System.Private.CoreLib", "6.0.0.0", "System.Security.Cryptography", "SHA256", "System.Security.Cryptography.Algorithms", "6.0.0.0")]
    public void Type_ScopesTheReferenceByCoreLibraryAndVersion(
        string corlib, string corlibVersion, string ns, string name, string expectedScope, string expectedVersion)
    {
        var token = corlib == "mscorlib" ? "b77a5c561934e089" : corlib == "netstandard" ? "cc7b13ffcd2ddd51" : PublicKeyToken;
        var module = ModuleWithCorlib(corlib, Version.Parse(corlibVersion), token);

        var type = FrameworkReferences.Type(module, ns, name);

        type.Namespace.String.ShouldBe(ns);
        type.Name.String.ShouldBe(name);
        type.Module.ShouldBe(module);
        var scope = type.ResolutionScope.ShouldBeAssignableTo<AssemblyRef>()!;
        scope.Name.String.ShouldBe(expectedScope);
        scope.Version.ShouldBe(Version.Parse(expectedVersion));
        if (scope.Name.String != corlib)
            scope.PublicKeyOrToken.Token.ToString().ShouldBe(token);
    }

    [Fact]
    public void Type_ReusesAReferenceTheModuleAlreadyHas()
    {
        // This test assembly is SDK-built and calls RuntimeEnvironment, so it references System.Runtime.InteropServices.
        using var module = ModuleDefMD.Load(typeof(FrameworkReferencesTests).Assembly.Location);
        var existing = module.GetAssemblyRefs().SingleOrDefault(a => a.Name == "System.Runtime.InteropServices");
        existing.ShouldNotBeNull();

        var scope = (AssemblyRef)FrameworkReferences.Type(module, "System", "DllNotFoundException").ResolutionScope;

        scope.ShouldBeSameAs(existing);
    }

    [Fact]
    public void Type_SynthesizesOneReferencePerAssembly()
    {
        var module = ModuleWithCorlib("System.Runtime", "6.0.0.0");

        var first = FrameworkReferences.Type(module, "System.Runtime.InteropServices", "Marshal").ResolutionScope;
        var second = FrameworkReferences.Type(module, "System", "DllNotFoundException").ResolutionScope;

        second.ShouldBeSameAs(first);
    }

    [Theory]
    [InlineData("System.Runtime", "10.0.0.0", null, true, true, true)]
    [InlineData("System.Runtime", "6.0.0.0", null, true, true, true)]
    [InlineData("System.Runtime", "5.0.0.0", null, true, false, true)]
    [InlineData("System.Runtime", "4.2.2.0", null, true, false, true)]
    [InlineData("System.Runtime", "4.2.1.0", null, false, false, true)]
    [InlineData("System.Private.CoreLib", "8.0.0.0", null, true, true, true)]
    [InlineData("System.Private.CoreLib", "4.0.0.0", null, false, false, true)]
    [InlineData("netstandard", "2.0.0.0", null, false, false, true)]
    [InlineData("mscorlib", "4.0.0.0", null, false, false, true)]
    [InlineData("mscorlib", "4.0.0.0", ".NETFramework,Version=v4.8", false, false, true)]
    [InlineData("mscorlib", "4.0.0.0", ".NETFramework,Version=v4.7.1", false, false, true)]
    [InlineData("mscorlib", "4.0.0.0", ".NETFramework,Version=v4.7", false, false, false)]
    [InlineData("mscorlib", "4.0.0.0", ".NETFramework,Version=v4.6.1,Profile=Client", false, false, false)]
    public void Supports_TellsWhatTheTargetHas(
        string corlib, string version, string? targetFramework, bool tickCount64, bool processPath, bool runtimeInformation)
    {
        var module = ModuleWithCorlib(corlib, Version.Parse(version));
        if (targetFramework is not null)
            AddTargetFramework(module, targetFramework);

        FrameworkReferences.SupportsTickCount64(module).ShouldBe(tickCount64, "TickCount64");
        FrameworkReferences.SupportsProcessPath(module).ShouldBe(processPath, "ProcessPath");
        FrameworkReferences.SupportsRuntimeInformation(module).ShouldBe(runtimeInformation, "RuntimeInformation");
    }

    private static void AddTargetFramework(ModuleDefUser module, string frameworkName)
    {
        var assembly = new AssemblyDefUser("Subject", new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);
        var attribute = FrameworkReferences.Type(module, "System.Runtime.Versioning", "TargetFrameworkAttribute");
        var constructor = new MemberRefUser(
            module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String), attribute);
        assembly.CustomAttributes.Add(new CustomAttribute(constructor, new[] { new CAArgument(module.CorLibTypes.String, frameworkName) }));
    }

    #endregion

    #region Injected code that depends on the target

    [Theory]
    [InlineData("System.Runtime", "10.0.0.0", true)]
    [InlineData("System.Runtime", "6.0.0.0", true)]
    [InlineData("System.Runtime", "5.0.0.0", false)]
    [InlineData("System.Runtime", "4.2.2.0", false)]
    [InlineData("mscorlib", "4.0.0.0", false)]
    [InlineData("netstandard", "2.0.0.0", false)]
    public async Task AntiTamper_UsesProcessPathOnlyWhereTheTargetHasIt(string corlib, string version, bool expected)
    {
        var module = ModuleWithCorlib(corlib, Version.Parse(version));
        var type = new TypeDefUser("Ns", "Program", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(type);
        var entryPoint = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static)
        {
            Body = new CilBody { Instructions = { Instruction.Create(OpCodes.Ret) } }
        };
        type.Methods.Add(entryPoint);
        module.EntryPoint = entryPoint;
        var context = PipelineContext.ForAssembly(
            module, new ObfySettings { Level = ObfuscationLevel.Custom, Protection = { AntiTamper = { Enabled = true } } });

        var result = await new AntiTamperObfuscator(new Mock<ILogger<AntiTamperObfuscator>>().Object).ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        var verify = module.Types.Single(t => t.Name == "<AntiTamper>").Methods.Single(m => m.Name == "Verify");
        MemberNames(verify).Contains("get_ProcessPath").ShouldBe(expected);
    }

    [Theory]
    [InlineData("System.Runtime", "10.0.0.0", null, "get_ProcessArchitecture")]
    [InlineData("System.Runtime", "6.0.0.0", null, "get_ProcessArchitecture")]
    [InlineData("netstandard", "2.0.0.0", null, "get_ProcessArchitecture")]
    [InlineData("mscorlib", "4.0.0.0", ".NETFramework,Version=v4.8", "get_ProcessArchitecture")]
    [InlineData("mscorlib", "4.0.0.0", ".NETFramework,Version=v4.6.1", "get_Size")]
    public async Task AntiDump_ChecksTheArchitectureWithWhatTheTargetHas(
        string corlib, string version, string? targetFramework, string expectedMember)
    {
        var module = ModuleWithCorlib(corlib, Version.Parse(version), corlib == "mscorlib" ? "b77a5c561934e089" : PublicKeyToken);
        if (targetFramework is not null)
            AddTargetFramework(module, targetFramework);
        module.Types.Add(new TypeDefUser("Ns", "Program", module.CorLibTypes.Object.TypeDefOrRef));
        var context = PipelineContext.ForAssembly(
            module, new ObfySettings { Level = ObfuscationLevel.Custom, Protection = { AntiDump = true } });

        var result = await new AntiDumpObfuscator(new Mock<ILogger<AntiDumpObfuscator>>().Object).ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        var neutralize = module.Types.Single(t => t.Name == "<AntiDump>").Methods.Single(m => m.Name == "NeutralizeDumpers");
        var members = MemberNames(neutralize);
        members.ShouldContain(expectedMember);
        if (expectedMember == "get_Size")
            members.ShouldNotContain("get_ProcessArchitecture");
    }

    private static List<string> MemberNames(MethodDef method) =>
        method.Body.Instructions
            .Select(i => i.Operand)
            .OfType<IMethod>()
            .Select(m => m.Name.String)
            .ToList();

    #endregion

    #region The tables against the real frameworks

    [TargetFrameworkFact("netcoreapp3.1")]
    public Task NetCoreApp31_ProtectedOutput_ResolvesOnTheTarget() => AssertTargetAsync("netcoreapp3.1");

    [TargetFrameworkFact("net5.0")]
    public Task Net5_ProtectedOutput_ResolvesOnTheTarget() => AssertTargetAsync("net5.0");

    [TargetFrameworkFact("net6.0")]
    public Task Net6_ProtectedOutput_ResolvesOnTheTarget() => AssertTargetAsync("net6.0");

    [TargetFrameworkFact("net7.0")]
    public Task Net7_ProtectedOutput_ResolvesOnTheTarget() => AssertTargetAsync("net7.0");

    [TargetFrameworkFact("net8.0")]
    public Task Net8_ProtectedOutput_ResolvesOnTheTarget() => AssertTargetAsync("net8.0");

    [TargetFrameworkFact("net9.0")]
    public Task Net9_ProtectedOutput_ResolvesOnTheTarget() => AssertTargetAsync("net9.0");

    [TargetFrameworkFact("net10.0")]
    public Task Net10_ProtectedOutput_ResolvesOnTheTarget() => AssertTargetAsync("net10.0");

    [TargetFrameworkFact("net461")]
    public Task Net461_ProtectedOutput_ResolvesOnTheTarget() => AssertTargetAsync("net461");

    [TargetFrameworkFact("net48")]
    public Task Net48_ProtectedOutput_ResolvesOnTheTarget() => AssertTargetAsync("net48");

    [TargetFrameworkFact("netstandard2.0@netcoreapp3.1")]
    public Task NetStandard20_ProtectedOutput_ResolvesOnNetCoreApp31() => AssertTargetAsync("netstandard2.0@netcoreapp3.1");

    [TargetFrameworkFact("netstandard2.0@net6.0")]
    public Task NetStandard20_ProtectedOutput_ResolvesOnNet6() => AssertTargetAsync("netstandard2.0@net6.0");

    [TargetFrameworkFact("netstandard2.0@net10.0")]
    public Task NetStandard20_ProtectedOutput_ResolvesOnNet10() => AssertTargetAsync("netstandard2.0@net10.0");

    [TargetFrameworkFact("netstandard2.0@net48")]
    public Task NetStandard20_ProtectedOutput_ResolvesOnNet48() => AssertTargetAsync("netstandard2.0@net48");

    /// <summary>
    /// Every entry of the routing tables, and then an app compiled against the target's reference assemblies (with a
    /// resource, a resx read through <c>ResourceManager.GetString</c>, a sibling assembly and code the VM can take)
    /// protected with each technique alone and with all of them, must reference only types and members the target's
    /// own assemblies resolve, at versions they can bind to.
    /// </summary>
    private static async Task AssertTargetAsync(string name)
    {
        var target = TargetFramework.Find(name)
                     ?? throw new InvalidOperationException($"The reference assemblies of {name} (the running framework) are missing.");
        var problems = new List<string>();
        using var facades = new FacadeSet(target.RuntimeDirectories);

        // The table, entry by entry, on the frameworks that use it
        var isDotNetCore = System.Text.RegularExpressions.Regex.IsMatch(name, @"^(netcoreapp\d\.\d|net\d+\.0)$");
        if (isDotNetCore && facades.AssemblyVersion("System.Runtime") is { } runtimeVersion)
        {
            var module = ModuleWithCorlib("System.Runtime", runtimeVersion);
            foreach (var fullName in FrameworkReferences.RoutedTypes)
            {
                var dot = fullName.LastIndexOf('.');
                var scope = (AssemblyRef)FrameworkReferences.Type(module, fullName[..dot], fullName[(dot + 1)..]).ResolutionScope;
                if (facades.ResolveType(scope.Name.String, fullName) is null)
                    problems.Add($"table: {fullName} is not carried by {scope.Name}");
                else if (facades.AssemblyVersion(scope.Name.String) is { } available && available < scope.Version)
                    problems.Add($"table: {fullName} is referenced at {scope.Version} but {scope.Name} is {available}");
            }
        }

        // Real protected output
        var directory = Path.Combine(Path.GetTempPath(), "obfy-fwref-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = RichSdkInput.Create(target, directory);
            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();
            var service = container.Resolve<IObfuscationService>();
            var own = new HashSet<string> { RichSdkInput.AppName, RichSdkInput.SiblingName };

            foreach (var technique in ProtectionCases.All.Keys)
            {
                var output = Path.Combine(directory, "out-" + technique, RichSdkInput.AppName + ".dll");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var result = await service.ObfuscateAsync(input, output, ProtectionCases.SettingsFor(technique));
                if (!result.Success)
                {
                    problems.Add($"{technique}: the protection failed: {result.ErrorMessage}");
                    continue;
                }

                using var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output));
                if (ProtectionCases.InjectsRuntimeType(technique) &&
                    !protectedModule.Types.Any(t => t.Namespace == "Obfy.Runtime" || t.Namespace.String.StartsWith("Obfy.Runtime.", StringComparison.Ordinal)))
                {
                    problems.Add($"{technique}: injected nothing, so this check proves nothing about it");
                }

                problems.AddRange(facades.Unresolved(protectedModule, own).Select(p => $"{technique}: {p}"));
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* ignore */ }
        }

        problems.ShouldBeEmpty(
            $"On {name}, protected output references what the target does not have:{Environment.NewLine}" +
            string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// A real SDK-built assembly (Settings.Core, core library System.Runtime) protected with every technique that
    /// injects code resolves on the runtime these tests run on.
    /// </summary>
    [Fact]
    public async Task SdkBuiltAssembly_AfterProtection_ReferencesOnlyWhatTheRunningFrameworkHas()
    {
        var input = typeof(Settings.Core.SettingsServiceBase<>).Assembly.Location;
        var directory = Path.Combine(Path.GetTempPath(), "obfy-fwref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var running = TargetFramework.Find($"net{Environment.Version.Major}.0")!;
            using var facades = new FacadeSet(running.RuntimeDirectories);
            HashSet<string> inputsOwnDependencies;
            using (var original = ModuleDefMD.Load(File.ReadAllBytes(input)))
            {
                original.CorLibTypes.AssemblyRef.Name.String.ShouldBe(
                    "System.Runtime", "the input must be built the way an SDK app is");
                // The packages the input already depends on (Microsoft.Extensions.*) are not in the shared framework.
                inputsOwnDependencies = original.GetAssemblyRefs()
                    .Select(a => a.Name.String)
                    .Where(n => !facades.HasAssembly(n))
                    .ToHashSet();
            }

            var settings = new ObfySettings { Level = ObfuscationLevel.Aggressive };
            settings.Watermark.Enabled = true;
            settings.Watermark.Id = "framework-references";
            settings.Virtualization.Enabled = true;

            var output = Path.Combine(directory, "Settings.Core.dll");
            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using (var container = builder.Build())
            {
                var result = await container.Resolve<IObfuscationService>().ObfuscateAsync(input, output, settings);
                result.Success.ShouldBeTrue(result.ErrorMessage);
            }

            using var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output));
            facades.Unresolved(protectedModule, inputsOwnDependencies).ShouldBeEmpty();
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* ignore */ }
        }
    }

    #endregion
}

/// <summary>
/// A fact about one target framework. It is skipped, with the reason in the test output, when this machine lacks the
/// reference assemblies or the runtime to check it. The framework the tests run on is never skipped.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TargetFrameworkFactAttribute : FactAttribute
{
    public TargetFrameworkFactAttribute(string target)
    {
        if (target != $"net{Environment.Version.Major}.0" && TargetFramework.Find(target) is null)
            Skip = $"The reference assemblies or runtime of {target} are not on this machine.";
    }
}
