using System.Reflection;
using System.Text.Json;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// The CLI is the <c>Obfy</c> dotnet tool, and <c>obfy --version</c> prints the informational version of
/// <c>obfy.dll</c> (the entry assembly of the real tool; in these tests it would be the test host). That version
/// comes from <c>version.json</c> through <c>Directory.Build.props</c>, never from the project file.
/// </summary>
public class ProductVersionTests
{
    [Fact]
    public void CliAssembly_CarriesTheVersionFromVersionJson()
    {
        var expected = RepoVersion.Read();
        var assembly = typeof(Program).Assembly;

        assembly.GetName().Name.ShouldBe("obfy");
        var version = assembly.GetName().Version.ShouldNotBeNull();
        $"{version.Major}.{version.Minor}.{version.Build}".ShouldBe(expected);
        version.Revision.ShouldBe(0);

        // A -p:Version prerelease suffix (release builds) may follow the numbers; build metadata is not printed.
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            .ShouldNotBeNull().InformationalVersion;
        informational.ShouldStartWith(expected);
    }

    [Fact]
    public void CoreAssembly_CarriesTheSameVersionAsTheCli()
    {
        var core = typeof(Obfy.Core.Models.ObfySettings).Assembly;

        core.GetName().Version.ShouldBe(typeof(Program).Assembly.GetName().Version);
        core.GetCustomAttribute<AssemblyInformationalVersionAttribute>().ShouldNotBeNull()
            .InformationalVersion.ShouldStartWith(RepoVersion.Read());
    }
}

/// <summary><c>version.json</c> at the repository root as <c>major.minor.patch</c>.</summary>
internal static class RepoVersion
{
    public static string Read()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
            directory = directory.Parent;
        directory.ShouldNotBeNull($"Could not locate Obfy.sln from {AppContext.BaseDirectory}");

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "version.json")));
        var root = document.RootElement;
        return $"{root.GetProperty("major").GetInt32()}.{root.GetProperty("minor").GetInt32()}.{root.GetProperty("patch").GetInt32()}";
    }
}
