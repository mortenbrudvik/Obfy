using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

/// <summary>The limit's resolution needs no process, so these run everywhere.</summary>
public class CliProcessRunnerTimeoutTests
{
    [Fact]
    public void ResolveTimeout_UsesTheDefault_ForMissingOrNonPositiveValues()
    {
        var expected = TimeSpan.FromMinutes(CliProcessRunner.DefaultTimeoutMinutes);

        CliProcessRunner.ResolveTimeout(null).ShouldBe(expected);
        CliProcessRunner.ResolveTimeout(0).ShouldBe(expected);
        CliProcessRunner.ResolveTimeout(-5).ShouldBe(expected);
    }

    [Fact]
    public void ResolveTimeout_UsesTheConfiguredMinutes_UpToTheMaximum()
    {
        CliProcessRunner.ResolveTimeout(5).ShouldBe(TimeSpan.FromMinutes(5));
        CliProcessRunner.ResolveTimeout(CliProcessRunner.MaxTimeoutMinutes)
            .ShouldBe(TimeSpan.FromMinutes(CliProcessRunner.MaxTimeoutMinutes));
        CliProcessRunner.ResolveTimeout(int.MaxValue)
            .ShouldBe(TimeSpan.FromMinutes(CliProcessRunner.MaxTimeoutMinutes));
    }

    [Fact]
    public void DefaultTimeout_IsGenerousEnoughForLargeSolutions()
    {
        CliProcessRunner.DefaultTimeoutMinutes.ShouldBeGreaterThanOrEqualTo(10);
    }
}
