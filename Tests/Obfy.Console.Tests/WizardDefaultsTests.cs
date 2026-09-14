using System.Text.Json;
using Obfy.Console.Wizard;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Console.Tests;

public class WizardDefaultsTests
{
    [Fact]
    public void ApplyUseCaseDefaults_Unity_SetsIl2CppProfile()
    {
        var context = new WizardContext
        {
            UseCase = "Game (Unity)",
            Settings = new ObfySettings()
        };

        ConfigurationWizard.ApplyUseCaseDefaults(context);

        context.Settings.RuntimeProfile.ShouldBe(RuntimeProfile.UnityIl2Cpp);
        context.Settings.Exclusions.Namespaces.ShouldContain("UnityEngine");
        context.Settings.Exclusions.Namespaces.ShouldContain("UnityEngine.*");
        context.Settings.Exclusions.Namespaces.ShouldContain("Unity");
        context.Settings.Exclusions.Namespaces.ShouldContain("Unity.*");
    }

    [Fact]
    public void ApplyUseCaseDefaults_Desktop_SetsPreserveXaml()
    {
        var context = new WizardContext
        {
            UseCase = "Desktop Application",
            Settings = new ObfySettings()
        };

        ConfigurationWizard.ApplyUseCaseDefaults(context);

        context.Settings.SymbolRenaming.PreserveXaml.ShouldBeTrue();
    }

    [Fact]
    public void ApplyUseCaseDefaults_Blazor_SetsBlazorWasmProfile()
    {
        var context = new WizardContext
        {
            UseCase = "Blazor WebAssembly",
            Settings = new ObfySettings()
        };

        ConfigurationWizard.ApplyUseCaseDefaults(context);

        context.Settings.RuntimeProfile.ShouldBe(RuntimeProfile.BlazorWasm);
    }

    [Fact]
    public void ApplyUseCaseDefaults_Maui_SetsPreserveXaml()
    {
        var context = new WizardContext
        {
            UseCase = "MAUI / Mobile",
            Settings = new ObfySettings()
        };

        ConfigurationWizard.ApplyUseCaseDefaults(context);

        context.Settings.SymbolRenaming.PreserveXaml.ShouldBeTrue();
    }

    [Fact]
    public void ApplyUseCaseDefaults_ClassLibrary_PreservesPublicApi()
    {
        var context = new WizardContext
        {
            UseCase = "Class Library / NuGet Package",
            Settings = new ObfySettings()
        };

        ConfigurationWizard.ApplyUseCaseDefaults(context);

        context.Settings.SymbolRenaming.PreservePublicApi.ShouldBeTrue();
    }

    [Fact]
    public void ApplyUseCaseDefaults_AspNet_ExcludesMvcAttributes()
    {
        var context = new WizardContext
        {
            UseCase = "Web Application (ASP.NET)",
            Settings = new ObfySettings()
        };

        ConfigurationWizard.ApplyUseCaseDefaults(context);

        context.Settings.Exclusions.Attributes.ShouldContain("Microsoft.AspNetCore.Mvc.RouteAttribute");
        context.Settings.Exclusions.Attributes.ShouldContain("Microsoft.AspNetCore.Mvc.ApiControllerAttribute");
    }

    [Fact]
    public void ApplyUseCaseDefaults_IsPublicApi_PreservesPublicNames()
    {
        var context = new WizardContext
        {
            UseCase = "Desktop Application",
            IsPublicApi = true,
            Settings = new ObfySettings()
        };

        ConfigurationWizard.ApplyUseCaseDefaults(context);

        context.Settings.SymbolRenaming.PreservePublicApi.ShouldBeTrue();
        context.Settings.SymbolRenaming.PreserveXaml.ShouldBeTrue();
    }

    [Fact]
    public async Task GenerateConfigAsync_WritesUseCaseDefaultsToNewFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-wiz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "obfy.json");
        try
        {
            var context = new WizardContext
            {
                UseCase = "Game (Unity)",
                Settings = new ObfySettings(),
                OutputFile = new FileInfo(path)
            };

            var ok = await ConfigurationWizard.GenerateConfigAsync(context);

            ok.ShouldBeTrue();
            File.Exists(path).ShouldBeTrue();
            var settings = JsonSerializer.Deserialize<ObfySettings>(
                await File.ReadAllTextAsync(path),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            settings.ShouldNotBeNull();
            settings!.RuntimeProfile.ShouldBe(RuntimeProfile.UnityIl2Cpp);
            settings.Exclusions.Namespaces.ShouldContain("UnityEngine");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
