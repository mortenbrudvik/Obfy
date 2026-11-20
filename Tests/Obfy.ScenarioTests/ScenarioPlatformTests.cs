using Shouldly;

namespace Obfy.ScenarioTests;

public class ScenarioPlatformTests
{
    [Theory]
    [InlineData("dotnet", "build app.csproj -c Release", true)]
    [InlineData("dotnet.exe", "publish app.csproj", true)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", "build app.csproj", true)]
    [InlineData("dotnet", "build app.csproj -p:Platform=x64", false)]
    [InlineData("dotnet", "build app.csproj /p:Platform=AnyCPU", false)]
    [InlineData("dotnet", "build app.csproj --property:Platform=x64", false)]
    [InlineData("dotnet", "build app.csproj -p:PlatformTarget=x64", true)]
    [InlineData("app.exe", "", false)]
    [InlineData("cmd.exe", "/c echo hi", false)]
    public void ShouldStripInheritedPlatform_OnlyForDotnetWithoutAPlatformProperty(
        string fileName, string arguments, bool strip)
    {
        ScenarioHarness.ShouldStripInheritedPlatform(fileName, arguments).ShouldBe(strip);
    }

    [Fact]
    public void RunProcess_KeepsPlatformUnlessDotnetOmitsIt()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "obfy-platform-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previousPlatform = Environment.GetEnvironmentVariable("Platform");
        try
        {
            var fakeDotnet = Path.Combine(dir, "dotnet.cmd");
            File.WriteAllText(fakeDotnet, "@echo off\r\necho [%Platform%]\r\nexit /b 0\r\n");
            Environment.SetEnvironmentVariable("Platform", "x64");

            var stripped = ScenarioHarness.RunProcess(fakeDotnet, "build app.csproj -c Release", dir, 20_000);
            var kept = ScenarioHarness.RunProcess(fakeDotnet, "build app.csproj -p:Platform=x64", dir, 20_000);
            var other = ScenarioHarness.RunProcess("cmd.exe", "/d /c echo [%Platform%]", dir, 20_000);

            stripped.ExitCode.ShouldBe(0, stripped.StdOut + stripped.StdErr);
            stripped.StdOut.Trim().ShouldBe("[]");
            kept.StdOut.Trim().ShouldBe("[x64]");
            other.StdOut.Trim().ShouldBe("[x64]");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Platform", previousPlatform);
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }
}
