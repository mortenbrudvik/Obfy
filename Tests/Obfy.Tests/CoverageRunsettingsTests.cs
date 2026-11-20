using Shouldly;

namespace Obfy.Tests;

public class CoverageRunsettingsTests
{
    [Fact]
    public void IncludeList_ContainsPackedBootstrap()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Obfy.sln")))
            dir = dir.Parent;

        dir.ShouldNotBeNull();
        var text = File.ReadAllText(Path.Combine(dir.FullName, "coverage.runsettings"));
        text.ShouldContain("[Obfy.PackedBootstrap]*");
    }
}
