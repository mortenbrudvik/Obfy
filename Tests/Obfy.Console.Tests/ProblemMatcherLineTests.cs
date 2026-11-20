using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;

namespace Obfy.Console.Tests;

public class ProblemMatcherLineTests
{
    [Fact]
    public void PackageJson_CapturesFileLineAndMessage()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Obfy.sln")))
            dir = dir.Parent;

        dir.ShouldNotBeNull();
        var json = File.ReadAllText(Path.Combine(dir.FullName, "Src", "Obfy.VSCode", "package.json"));
        using var doc = JsonDocument.Parse(json);
        var pattern = doc.RootElement
            .GetProperty("contributes")
            .GetProperty("problemMatchers")[0]
            .GetProperty("pattern");

        pattern.TryGetProperty("file", out _).ShouldBeTrue();
        pattern.TryGetProperty("line", out _).ShouldBeTrue();

        var line = ProblemMatcherLine.Format(@"C:\src\App.dll", "error", "bad image");
        var match = Regex.Match(line, pattern.GetProperty("regexp").GetString()!);
        match.Success.ShouldBeTrue();
        match.Groups[pattern.GetProperty("file").GetInt32()].Value.ShouldBe(@"C:\src\App.dll");
        match.Groups[pattern.GetProperty("line").GetInt32()].Value.ShouldBe("1");
        match.Groups[pattern.GetProperty("column").GetInt32()].Value.ShouldBe("1");
        match.Groups[pattern.GetProperty("severity").GetInt32()].Value.ShouldBe("error");
        match.Groups[pattern.GetProperty("message").GetInt32()].Value.ShouldBe("bad image");
    }
}
