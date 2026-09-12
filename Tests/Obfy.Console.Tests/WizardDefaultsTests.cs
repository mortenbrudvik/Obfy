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
        context.Settings.Exclusions.Namespaces.ShouldContain("UnityEngine.*");
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
}
