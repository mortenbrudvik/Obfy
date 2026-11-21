using System.Reflection;
using System.Runtime.CompilerServices;
using Obfy.Core.Services;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// The CLI (<c>obfy.dll</c>) is a separate assembly that the installer build can protect on its own, so it must reach Core
/// through public members only. It needs exactly three: the default output name, the files packing writes beside an output
/// and the file-identity comparison. Nothing is shared by <c>InternalsVisibleTo</c>.
/// </summary>
public class CliPublicSurfaceTests
{
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

    [Fact]
    public void Core_DoesNotShareItsInternalsWithTheCli()
    {
        var friends = typeof(ObfuscationService).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(static a => a.AssemblyName.Split(',')[0].Trim())
            .ToList();

        friends.ShouldNotContain("obfy");
    }

    [Fact]
    public void TheThreeMembersTheCliUses_ArePublic()
    {
        typeof(PathIdentity).IsPublic.ShouldBeTrue("PathIdentity");
        typeof(PathIdentity).GetMethod(nameof(PathIdentity.SameFile), PublicStatic).ShouldNotBeNull();
        typeof(ObfuscationService).GetMethod(nameof(ObfuscationService.GenerateOutputPath), PublicStatic).ShouldNotBeNull();
        typeof(PackingApplicator).IsPublic.ShouldBeTrue("PackingApplicator");
        typeof(PackingApplicator).GetMethod(nameof(PackingApplicator.SidecarPaths), PublicStatic).ShouldNotBeNull();
    }

    [Fact]
    public void GenerateOutputPath_IsNameDotObfuscatedBesideTheInput()
    {
        var folder = Path.Combine(Path.GetTempPath(), "obfy-surface");

        ObfuscationService.GenerateOutputPath(Path.Combine(folder, "App.dll")).ShouldBe(Path.Combine(folder, "App.obfuscated.dll"));
        ObfuscationService.GenerateOutputPath(Path.Combine(folder, "Tool.EXE")).ShouldBe(Path.Combine(folder, "Tool.obfuscated.EXE"));
        ObfuscationService.GenerateOutputPath(Path.Combine(folder, "Two.Parts.cs")).ShouldBe(Path.Combine(folder, "Two.Parts.obfuscated.cs"));
        ObfuscationService.GenerateOutputPath("App.dll").ShouldBe("App.obfuscated.dll");
    }
}
