using Shouldly;

namespace Obfy.ScenarioTests;

public class ScenarioTimeoutTests
{
    [Fact]
    public void Defaults_GiveColdRunnersMoreTime()
    {
        ScenarioHarness.DefaultBuildTimeoutMs.ShouldBe(180_000);
        ScenarioHarness.DefaultPublishTimeoutMs.ShouldBe(300_000);
        ScenarioHarness.DefaultRunTimeoutMs.ShouldBe(60_000);
    }

    [Theory]
    [InlineData(null, 20_000)]
    [InlineData("", 20_000)]
    [InlineData("1", 20_000)]
    [InlineData("0", 20_000)]
    [InlineData("-3", 20_000)]
    [InlineData("nope", 20_000)]
    [InlineData("2", 40_000)]
    [InlineData("11", 200_000)]
    public void ScaleTimeout_AppliesFactorBetweenTwoAndTen(string? factor, int expected)
    {
        ScenarioHarness.ScaleTimeout(20_000, factor).ShouldBe(expected);
    }

    [Fact]
    public void ScaleTimeout_CapsTheProductAtIntMax()
    {
        ScenarioHarness.ScaleTimeout(int.MaxValue, "10").ShouldBe(int.MaxValue);
    }

    [Fact]
    public void ScaleTimeout_ReadsTheEnvironmentVariable()
    {
        var previous = Environment.GetEnvironmentVariable(ScenarioHarness.TimeoutFactorVariable);
        try
        {
            Environment.SetEnvironmentVariable(ScenarioHarness.TimeoutFactorVariable, "3");
            ScenarioHarness.ScaleTimeout(1_000).ShouldBe(3_000);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScenarioHarness.TimeoutFactorVariable, previous);
        }
    }
}
