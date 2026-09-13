using Obfy.VisualStudio.Services;
using Obfy.VisualStudio.UI;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class SettingsDialogViewModelTests
{
    [Fact]
    public void GetSettings_Aggressive_KeepsMethodEncryption()
    {
        var vm = new SettingsDialogViewModel(ObfySettings.ForLevel(ObfuscationLevel.Aggressive), "P");
        var saved = vm.GetSettings();
        saved.MethodEncryption.ShouldBeTrue();
        saved.ProxyExternalCalls.ShouldBeFalse();
    }

    [Fact]
    public void GetSettings_AfterSwitchingToAggressive_WritesMethodEncryption()
    {
        var vm = new SettingsDialogViewModel(ObfySettings.ForLevel(ObfuscationLevel.Standard), "P");
        vm.GetSettings().MethodEncryption.ShouldBeFalse();
        vm.Level = ObfuscationLevel.Aggressive;
        var saved = vm.GetSettings();
        saved.MethodEncryption.ShouldBeTrue();
        saved.ProxyExternalCalls.ShouldBeFalse();
    }
}
