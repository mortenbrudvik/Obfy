using System;
using System.IO;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Obfy.VisualStudio.Services;
using Obfy.VisualStudio.UI;

namespace Obfy.VisualStudio.Commands;

/// <summary>
/// Command that opens the settings dialog for the selected project
/// </summary>
[Command(PackageIds.cmdidOpenSettings)]
internal sealed class OpenSettingsCommand : BaseCommand<OpenSettingsCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var output = ObfyPackage.OutputService;
        var settingsService = ObfyPackage.ProjectSettingsService;

        if (settingsService == null)
        {
            await VS.MessageBox.ShowErrorAsync("Obfy", "Settings service not initialized");
            return;
        }

        var project = await VS.Solutions.GetActiveProjectAsync();
        if (project == null)
        {
            await VS.MessageBox.ShowErrorAsync("Obfy", "No project selected");
            return;
        }

        // Load existing settings or create defaults
        var settings = await settingsService.LoadSettingsAsync(project);

        if (settings == null)
        {
            var options = ObfyPackage.Options;
            var level = options?.DefaultLevel ?? ObfuscationLevel.Standard;
            settings = ObfySettings.ForLevel(level);

            // Apply default post-build setting
            settings.PostBuildEnabled = options?.EnablePostBuildByDefault ?? false;
        }

        // Show the settings dialog
        var viewModel = new SettingsDialogViewModel(settings, project.Name);
        var dialog = new SettingsDialog(viewModel);

        var result = dialog.ShowDialog();

        if (result == true)
        {
            // Save settings
            await settingsService.SaveSettingsAsync(project, viewModel.GetSettings());

            output?.Info($"Settings saved for {project.Name}");
            await VS.StatusBar.ShowMessageAsync("Obfy settings saved");
        }
    }

    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var project = await VS.Solutions.GetActiveProjectAsync();
            Command.Visible = project != null;
        });
    }
}
