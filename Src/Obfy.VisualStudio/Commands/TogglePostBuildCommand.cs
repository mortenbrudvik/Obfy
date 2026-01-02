using System;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;

namespace Obfy.VisualStudio.Commands;

/// <summary>
/// Command that toggles post-build obfuscation for the selected project
/// </summary>
[Command(PackageIds.cmdidTogglePostBuild)]
internal sealed class TogglePostBuildCommand : BaseCommand<TogglePostBuildCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var output = ObfyPackage.OutputService;
        var settingsService = ObfyPackage.ProjectSettingsService;

        if (settingsService == null)
        {
            return;
        }

        var project = await VS.Solutions.GetActiveProjectAsync();
        if (project == null)
        {
            return;
        }

        // Toggle the post-build setting
        var currentlyEnabled = await settingsService.IsPostBuildEnabledAsync(project);
        var newState = !currentlyEnabled;

        await settingsService.SetPostBuildEnabledAsync(project, newState);

        // Update command text
        Command.Text = newState ? "Disable Post-Build Obfuscation" : "Enable Post-Build Obfuscation";
        Command.Checked = newState;

        // Log the change
        output?.Info($"Post-build obfuscation {(newState ? "enabled" : "disabled")} for {project.Name}");
        await VS.StatusBar.ShowMessageAsync($"Post-build obfuscation {(newState ? "enabled" : "disabled")}");
    }

    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var project = await VS.Solutions.GetActiveProjectAsync();
            if (project == null)
            {
                Command.Visible = false;
                return;
            }

            Command.Visible = true;

            var settingsService = ObfyPackage.ProjectSettingsService;
            if (settingsService != null)
            {
                var enabled = await settingsService.IsPostBuildEnabledAsync(project);
                Command.Text = enabled ? "Disable Post-Build Obfuscation" : "Enable Post-Build Obfuscation";
                Command.Checked = enabled;
            }
        });
    }
}
