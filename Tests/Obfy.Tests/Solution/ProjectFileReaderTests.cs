using Obfy.Core.Services.Solution;
using Shouldly;

namespace Obfy.Tests.Solution;

public class ProjectFileReaderTests
{
    [Fact]
    public void Read_WpfWinExe_SetsFlagsAndFramework()
    {
        var path = WriteTempProject("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
              </PropertyGroup>
            </Project>
            """);

        try
        {
            var info = ProjectFileReader.Read(path);

            info.Path.ShouldBe(path);
            info.AssemblyName.ShouldBe("App");
            info.OutputType.ShouldBe("WinExe");
            info.TargetFrameworks.ShouldBe(new[] { "net8.0-windows" });
            info.UseWpf.ShouldBeTrue();
            info.UseWinForms.ShouldBeFalse();
            info.UseMaui.ShouldBeFalse();
            info.PublishAot.ShouldBeFalse();
            info.IsBlazorWasm.ShouldBeFalse();
            info.IsAspNetWeb.ShouldBeFalse();
            info.IsTest.ShouldBeFalse();
            info.ReferencesUnity.ShouldBeFalse();
            info.ProjectReferences.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Read_TestSdkPackage_NamedApp_IsTest()
    {
        var path = WriteTempProject("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.0.0" />
              </ItemGroup>
            </Project>
            """);

        try
        {
            var info = ProjectFileReader.Read(path);

            info.AssemblyName.ShouldBe("App");
            info.IsTest.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Read_Contest_WithoutTestPackages_IsNotTest()
    {
        var path = WriteTempProject("Contest.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        try
        {
            var info = ProjectFileReader.Read(path);

            info.AssemblyName.ShouldBe("Contest");
            info.IsTest.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Read_AssemblyNameOverride_UsesPropertyNotFileName()
    {
        var path = WriteTempProject("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <AssemblyName>Contoso.App</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        try
        {
            ProjectFileReader.Read(path).AssemblyName.ShouldBe("Contoso.App");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Read_BlazorWebAssemblySdk_IsBlazorWasm()
    {
        var path = WriteTempProject("Client.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        try
        {
            var info = ProjectFileReader.Read(path);
            info.IsBlazorWasm.ShouldBeTrue();
            info.IsAspNetWeb.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Read_XunitPackage_NamedApp_IsTest()
    {
        var path = WriteTempProject("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.0" />
              </ItemGroup>
            </Project>
            """);

        try
        {
            ProjectFileReader.Read(path).IsTest.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static string WriteTempProject(string fileName, string contents)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-csproj-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, contents);
        return path;
    }
}
