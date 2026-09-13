using Obfy.Core.Services.Solution;
using Shouldly;

namespace Obfy.Tests.Solution;

public class SolutionFileParserTests
{
    [Fact]
    public void Parse_Sln_ReturnsAllProjectEntriesIncludingSolutionFolders()
    {
        var path = WriteTempFile(".sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App.Tests", "tests\App.Tests\App.Tests.csproj", "{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}"
            EndProject
            """);

        try
        {
            var refs = SolutionFileParser.Parse(path);

            refs.Count.ShouldBe(3);
            refs[0].ShouldBe(new SolutionProjectRef("App", @"src\App\App.csproj"));
            refs[1].ShouldBe(new SolutionProjectRef("src", "src"));
            refs[2].ShouldBe(new SolutionProjectRef("App.Tests", @"tests\App.Tests\App.Tests.csproj"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_Slnx_ReturnsProjectsWithNameFromFileName()
    {
        var path = WriteTempFile(".slnx", """
            <Solution>
              <Project Path="src/App/App.csproj" />
              <Project Path="tests/App.Tests/App.Tests.csproj" />
            </Solution>
            """);

        try
        {
            var refs = SolutionFileParser.Parse(path);

            refs.Count.ShouldBe(2);
            refs[0].ShouldBe(new SolutionProjectRef("App", "src/App/App.csproj"));
            refs[1].ShouldBe(new SolutionProjectRef("App.Tests", "tests/App.Tests/App.Tests.csproj"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_UnknownExtension_ThrowsArgumentException()
    {
        var path = WriteTempFile(".txt", "not a solution");

        try
        {
            Should.Throw<ArgumentException>(() => SolutionFileParser.Parse(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_MissingFile_ThrowsFileNotFoundException()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"obfy-missing-{Guid.NewGuid():N}.sln");
        Should.Throw<FileNotFoundException>(() => SolutionFileParser.Parse(missing));
    }

    [Fact]
    public void Parse_Slnx_IgnoresEmptyPath_AndAcceptsLowercasePathAttribute()
    {
        var path = WriteTempFile(".slnx", """
            <Solution>
              <Project Path="" />
              <Project path="lib/Lib.csproj" />
            </Solution>
            """);

        try
        {
            var refs = SolutionFileParser.Parse(path);

            refs.Count.ShouldBe(1);
            refs[0].ShouldBe(new SolutionProjectRef("Lib", "lib/Lib.csproj"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempFile(string extension, string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"obfy-sln-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, contents);
        return path;
    }
}
