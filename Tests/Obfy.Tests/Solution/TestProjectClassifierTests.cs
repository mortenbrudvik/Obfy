using Obfy.Core.Services.Solution;
using Shouldly;

namespace Obfy.Tests.Solution;

public class TestProjectClassifierTests
{
    [Theory]
    [InlineData("Obfy.Tests", true)]
    [InlineData("Obfy.Console.Tests", true)]
    [InlineData("MyApp.Test", true)]
    [InlineData("MyApp.Testing", true)]
    [InlineData("Test", true)]
    [InlineData("Contest", false)]
    [InlineData("MyApp", false)]
    [InlineData("Latest", false)]
    public void IsTestProjectName_MatchesSpec(string name, bool expected)
        => TestProjectClassifier.IsTestProjectName(name).ShouldBe(expected);
}
