using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services.Solution;
using Shouldly;

namespace Obfy.Tests.Solution;

public class SolutionAnalyzerTests
{
    [Fact]
    public void Analyze_AppLibAndTests_IncludesAppAndLib_SkipsTests_AndDoesNotPreserveLibPublicApi()
    {
        using var fixture = new TempDir();
        var sln = WriteSln(fixture, "App.sln",
            ("App", @"App\App.csproj"),
            ("Lib", @"Lib\Lib.csproj"),
            ("App.Tests", @"App.Tests\App.Tests.csproj"));

        WriteProject(fixture, @"App\App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteProject(fixture, @"Lib\Lib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        WriteProject(fixture, @"App.Tests\App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
            </Project>
            """);

        var appDll = fixture.WriteEmpty(@"App\bin\Release\net8.0\App.dll");
        var libDll = fixture.WriteEmpty(@"Lib\bin\Release\net8.0\Lib.dll");
        fixture.WriteEmpty(@"App.Tests\bin\Release\net8.0\App.Tests.dll");

        var session = new SolutionAnalyzer().Analyze(sln);

        session.SourcePath.ShouldBe(sln);
        session.Entries.Count.ShouldBe(3);

        var app = session.Entries.Single(e => e.ProjectName == "App");
        app.IsIncluded.ShouldBeTrue();
        app.SkipReason.ShouldBe(Obfy.Core.Models.Solution.SkipReason.None);
        app.OutputPath.ShouldBe(appDll);
        app.Hints.PreservePublicApi.ShouldBeFalse();

        var lib = session.Entries.Single(e => e.ProjectName == "Lib");
        lib.IsIncluded.ShouldBeTrue();
        lib.SkipReason.ShouldBe(Obfy.Core.Models.Solution.SkipReason.None);
        lib.OutputPath.ShouldBe(libDll);
        lib.Hints.PreservePublicApi.ShouldBeFalse();

        var tests = session.Entries.Single(e => e.ProjectName == "App.Tests");
        tests.IsIncluded.ShouldBeFalse();
        tests.SkipReason.ShouldBe(Obfy.Core.Models.Solution.SkipReason.SkipTest);
        tests.SkipMessage.ShouldBe("Test project");

        session.Included.Select(e => e.ProjectName).ShouldBe(new[] { "App", "Lib" }, ignoreOrder: true);
    }

    [Fact]
    public void Analyze_LibrariesOnly_PreservesPublicApiOnBoth()
    {
        using var fixture = new TempDir();
        var sln = WriteSln(fixture, "Libs.sln",
            ("LibA", @"LibA\LibA.csproj"),
            ("LibB", @"LibB\LibB.csproj"));

        WriteLibrary(fixture, @"LibA\LibA.csproj");
        WriteLibrary(fixture, @"LibB\LibB.csproj");
        fixture.WriteEmpty(@"LibA\bin\Release\net8.0\LibA.dll");
        fixture.WriteEmpty(@"LibB\bin\Release\net8.0\LibB.dll");

        var session = new SolutionAnalyzer().Analyze(sln);

        session.Included.Count().ShouldBe(2);
        session.Entries.ShouldAllBe(e => e.Hints.PreservePublicApi);
        session.Entries.ShouldAllBe(e => e.IsIncluded);
    }

    [Fact]
    public void Analyze_WpfAot_SetsPreserveXamlAndNativeAot()
    {
        using var fixture = new TempDir();
        var csproj = WriteProject(fixture, @"App\App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
                <PublishAot>true</PublishAot>
              </PropertyGroup>
            </Project>
            """);
        fixture.WriteEmpty(@"App\bin\Release\net8.0-windows\App.dll");

        var session = new SolutionAnalyzer().Analyze(csproj);

        var app = session.Entries.ShouldHaveSingleItem();
        app.IsIncluded.ShouldBeTrue();
        app.Hints.PreserveXaml.ShouldBeTrue();
        app.Hints.RuntimeProfile.ShouldBe(RuntimeProfile.NativeAot);
        app.Hints.PreservePublicApi.ShouldBeFalse();
    }

    [Fact]
    public void Analyze_MissingBin_SkipMissing()
    {
        using var fixture = new TempDir();
        var csproj = WriteLibrary(fixture, @"Lib\Lib.csproj");

        var session = new SolutionAnalyzer().Analyze(csproj);

        var entry = session.Entries.ShouldHaveSingleItem();
        entry.IsIncluded.ShouldBeFalse();
        entry.SkipReason.ShouldBe(Obfy.Core.Models.Solution.SkipReason.SkipMissing);
        entry.SkipMessage.ShouldBe("No built output in bin/Release or bin/Debug");
        entry.OutputPath.ShouldBeNull();
    }

    [Fact]
    public void Analyze_ContestLibrary_IsIncluded_NotSkipTest()
    {
        using var fixture = new TempDir();
        var csproj = WriteLibrary(fixture, @"Contest\Contest.csproj");
        var dll = fixture.WriteEmpty(@"Contest\bin\Release\net8.0\Contest.dll");

        var session = new SolutionAnalyzer().Analyze(csproj);

        var entry = session.Entries.ShouldHaveSingleItem();
        entry.IsIncluded.ShouldBeTrue();
        entry.ProjectName.ShouldBe("Contest");
        entry.SkipReason.ShouldBe(Obfy.Core.Models.Solution.SkipReason.None);
        entry.OutputPath.ShouldBe(dll);
        entry.Hints.PreservePublicApi.ShouldBeTrue();
    }

    [Fact]
    public void Analyze_UnsupportedExtension_ThrowsArgumentException()
    {
        using var fixture = new TempDir();
        var path = fixture.Write("notes.txt", "not a project");

        Should.Throw<ArgumentException>(() => new SolutionAnalyzer().Analyze(path));
    }

    [Fact]
    public void Analyze_SolutionFolderAndVcxproj_AreSkipUnsupported_MissingCsprojIsSkipMissingProject()
    {
        using var fixture = new TempDir();
        var sln = WriteSln(fixture, "App.sln",
            ("src", "src"),
            ("Native", @"Native\Native.vcxproj"),
            ("Ghost", @"Ghost\Ghost.csproj"),
            ("Lib", @"Lib\Lib.csproj"));

        Directory.CreateDirectory(Path.Combine(fixture.Root, "src"));
        fixture.Write(@"Native\Native.vcxproj", "<Project></Project>");
        WriteLibrary(fixture, @"Lib\Lib.csproj");
        fixture.WriteEmpty(@"Lib\bin\Release\net8.0\Lib.dll");

        var session = new SolutionAnalyzer().Analyze(sln);

        session.Entries.Count.ShouldBe(4);

        var folder = session.Entries.Single(e => e.ProjectName == "src");
        folder.IsIncluded.ShouldBeFalse();
        folder.SkipReason.ShouldBe(Obfy.Core.Models.Solution.SkipReason.SkipUnsupported);
        folder.SkipMessage.ShouldBe("Unsupported project type");

        var native = session.Entries.Single(e => e.ProjectName == "Native");
        native.IsIncluded.ShouldBeFalse();
        native.SkipReason.ShouldBe(Obfy.Core.Models.Solution.SkipReason.SkipUnsupported);
        native.SkipMessage.ShouldBe("Unsupported project type '.vcxproj'");

        var ghost = session.Entries.Single(e => e.ProjectName == "Ghost");
        ghost.IsIncluded.ShouldBeFalse();
        ghost.SkipReason.ShouldBe(Obfy.Core.Models.Solution.SkipReason.SkipMissingProject);
        ghost.SkipMessage.ShouldBe("Project file not found");

        var lib = session.Entries.Single(e => e.ProjectName == "Lib");
        lib.IsIncluded.ShouldBeTrue();
        lib.SkipReason.ShouldBe(Obfy.Core.Models.Solution.SkipReason.None);
    }

    private static string WriteSln(TempDir fixture, string fileName, params (string Name, string RelativePath)[] projects)
    {
        var lines = new List<string>
        {
            "Microsoft Visual Studio Solution File, Format Version 12.00"
        };

        foreach (var (name, relativePath) in projects)
        {
            var guid = Guid.NewGuid().ToString("D").ToUpperInvariant();
            lines.Add("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"" + name + "\", \"" + relativePath + "\", \"{" + guid + "}\"");
            lines.Add("EndProject");
        }

        return fixture.Write(fileName, string.Join(Environment.NewLine, lines));
    }

    private static string WriteLibrary(TempDir fixture, string relativePath)
        => WriteProject(fixture, relativePath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

    private static string WriteProject(TempDir fixture, string relativePath, string contents)
        => fixture.Write(relativePath, contents);
}

public class SolutionHintApplierTests
{
    [Fact]
    public void Apply_DoesNotOverwriteNonDefaultRuntimeProfile()
    {
        var settings = new ObfySettings { RuntimeProfile = RuntimeProfile.BlazorWasm };
        var hints = new ProjectSettingsHints
        {
            PreserveXaml = true,
            PreservePublicApi = false,
            RuntimeProfile = RuntimeProfile.NativeAot,
            AddUnityExcludes = true,
            AddAspNetMvcExcludes = true
        };

        SolutionHintApplier.Apply(settings, hints, forcePreservePublic: false);

        settings.RuntimeProfile.ShouldBe(RuntimeProfile.BlazorWasm);
        settings.SymbolRenaming.PreserveXaml.ShouldBeTrue();
        settings.SymbolRenaming.PreservePublicApi.ShouldBeFalse();
        settings.Exclusions.Namespaces.ShouldContain("UnityEngine");
        settings.Exclusions.Namespaces.ShouldContain("UnityEngine.*");
        settings.Exclusions.Namespaces.ShouldContain("Unity");
        settings.Exclusions.Namespaces.ShouldContain("Unity.*");
        settings.Exclusions.Attributes.ShouldContain("Microsoft.AspNetCore.Mvc.RouteAttribute");
        settings.Exclusions.Attributes.ShouldContain("Microsoft.AspNetCore.Mvc.ApiControllerAttribute");
        settings.Exclusions.Attributes.ShouldContain("Microsoft.AspNetCore.Mvc.HttpGetAttribute");
        settings.Exclusions.Attributes.ShouldContain("Microsoft.AspNetCore.Mvc.HttpPostAttribute");
        settings.Exclusions.Attributes.ShouldContain("Microsoft.AspNetCore.Mvc.HttpPutAttribute");
        settings.Exclusions.Attributes.ShouldContain("Microsoft.AspNetCore.Mvc.HttpDeleteAttribute");
    }

    [Fact]
    public void Apply_DefaultRuntimeProfile_TakesHint_AndForcePreservePublicWins()
    {
        var settings = new ObfySettings { RuntimeProfile = RuntimeProfile.Default };
        var hints = new ProjectSettingsHints
        {
            PreservePublicApi = false,
            RuntimeProfile = RuntimeProfile.NativeAot
        };

        SolutionHintApplier.Apply(settings, hints, forcePreservePublic: true);

        settings.RuntimeProfile.ShouldBe(RuntimeProfile.NativeAot);
        settings.SymbolRenaming.PreservePublicApi.ShouldBeTrue();
    }
}

internal sealed class TempDir : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"obfy-session-{Guid.NewGuid():N}");

    public TempDir() => Directory.CreateDirectory(Root);

    public string Write(string relativePath, string contents)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
        return full;
    }

    public string WriteEmpty(string relativePath)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, Array.Empty<byte>());
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
