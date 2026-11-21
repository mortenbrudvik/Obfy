using System;
using System.IO;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Obfy.VisualStudio.Services;

namespace Obfy.VisualStudio.Commands;

/// <summary>
/// Command that obfuscates the selected project's output assembly
/// </summary>
[Command(PackageIds.cmdidObfuscate)]
internal sealed class ObfuscateCommand : BaseCommand<ObfuscateCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var output = ObfyPackage.OutputService;
        var obfuscator = ObfyPackage.ObfuscationService;
        var settingsService = ObfyPackage.ProjectSettingsService;

        if (output == null || obfuscator == null || settingsService == null)
        {
            await VS.MessageBox.ShowErrorAsync("Obfy", "Extension services not initialized");
            return;
        }

        // Get the selected project
        var project = await VS.Solutions.GetActiveProjectAsync();
        if (project == null)
        {
            await VS.MessageBox.ShowErrorAsync("Obfy", "No project selected");
            return;
        }

        // Registers this run so closing the solution, disposing the package, a new build of the project or the
        // Cancel Obfuscation command can stop it. Disposed on every path out of this method.
        using var run = ObfyPackage.Runs.Begin(project.FullPath);

        // Show and activate output window
        var options = ObfyPackage.Options;
        if (options?.ShowOutputWindow ?? true)
        {
            await output.ActivateAsync();
        }

        await output.ClearAsync();
        output.Info($"Starting obfuscation for project: {project.Name}");

        try
        {
            // Get output assembly path
            var outputPath = await GetOutputAssemblyPathAsync(project);
            if (string.IsNullOrEmpty(outputPath) || !File.Exists(outputPath))
            {
                output.Error($"Output assembly not found. Build the project first.");
                await VS.MessageBox.ShowWarningAsync("Obfy", "Output assembly not found. Please build the project first.");
                return;
            }

            output.Info($"Assembly: {outputPath}");

            // Load project settings or use defaults
            var settings = await settingsService.LoadSettingsAsync(project);
            if (settings == null)
            {
                var level = options?.DefaultLevel ?? ObfuscationLevel.Standard;
                settings = ObfySettings.ForLevel(level);
                output.Info($"Using default settings (Level: {level})");
            }
            else
            {
                output.Info($"Using project settings (Level: {settings.Level})");
            }

            // Run obfuscation
            await VS.StatusBar.ShowProgressAsync("Obfuscating...", 1, 2);

            var assemblyPath = outputPath!;
            var projectDir = Path.GetDirectoryName(await project.GetAttributeAsync("FullPath"));
            var configPath = !string.IsNullOrEmpty(projectDir)
                ? settingsService.GetSettingsFilePath(projectDir)
                : null;
            if (configPath is not null && !File.Exists(configPath))
                configPath = null;

            ObfuscationResult result;
            try
            {
                result = await obfuscator.ObfuscateAsync(assemblyPath, assemblyPath, settings, run.Token, configPath);
            }
            finally
            {
                await VS.StatusBar.ClearAsync();
            }

            if (result.StopReason == ObfuscationStopReason.Cancelled)
            {
                output.Warning(result.ErrorMessage ?? ObfuscationResult.CancelledMessage);
                await VS.StatusBar.ShowMessageAsync("Obfuscation cancelled");
            }
            else if (result.StopReason == ObfuscationStopReason.TimedOut)
            {
                output.Error(result.ErrorMessage ?? "Obfuscation timed out.");
                await VS.StatusBar.ShowMessageAsync("Obfuscation timed out");
                await VS.MessageBox.ShowErrorAsync("Obfy", result.ErrorMessage ?? "Obfuscation timed out.");
            }
            else if (result.Success)
            {
                output.Success($"Obfuscation complete!");
                output.Info($"  Output: {result.OutputPath}");
                output.Info($"  Total transformations: {result.Statistics.TotalTransformations}");
                output.Info($"  Strings encrypted: {result.Statistics.StringsEncrypted}");
                output.Info($"  Symbols renamed: {result.Statistics.SymbolsRenamed}");
                output.Info($"  Elapsed: {result.ElapsedTime.TotalMilliseconds:F0}ms");

                await VS.StatusBar.ShowMessageAsync($"Obfuscation complete: {result.Statistics.TotalTransformations} transformations");
            }
            else
            {
                output.Error($"Obfuscation failed: {result.ErrorMessage}");
                await VS.MessageBox.ShowErrorAsync("Obfy", $"Obfuscation failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            output.Error($"Error: {ex.Message}");
            await VS.MessageBox.ShowErrorAsync("Obfy", $"An error occurred: {ex.Message}");
        }
    }

    protected override void BeforeQueryStatus(EventArgs e)
    {
        // Do not JoinableTaskFactory.Run here — it can deadlock the UI thread.
        Command.Visible = true;
    }

    private static async Task<string?> GetOutputAssemblyPathAsync(Project project)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        try
        {
            // Try to get the output path from project properties
            var outputPath = await project.GetAttributeAsync("OutputPath");
            var outputFileName = await project.GetAttributeAsync("OutputFileName");
            var projectDir = Path.GetDirectoryName(await project.GetAttributeAsync("FullPath"));

            return OutputAssemblyLocator.ResolveExisting(
                projectDir,
                outputPath,
                outputFileName,
                project.Name);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Package IDs for commands - must match the values in .vsct file
/// </summary>
internal static class PackageIds
{
    public const int cmdidObfuscate = 0x0100;
    public const int cmdidTogglePostBuild = 0x0101;
    public const int cmdidOpenSettings = 0x0102;
    public const int cmdidCancelObfuscation = 0x0103;
}
