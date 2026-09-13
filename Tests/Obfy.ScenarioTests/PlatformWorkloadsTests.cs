using System.Runtime.InteropServices;
using Shouldly;

namespace Obfy.ScenarioTests;

public class PlatformWorkloadsTests
{
    [Fact]
    public void CurrentWindowsRid_MatchesProcessArchitecture()
    {
        var expected = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X86 => "win-x86",
            _ => "win-x64"
        };
        PlatformWorkloads.CurrentWindowsRid().ShouldBe(expected);
    }
}
