using System;
using System.IO;
using System.Threading;
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

            var cts = new CancellationTokenSource();
            var result = await obfuscator.ObfuscateAsync(outputPath, null, settings, cts.Token);

            await VS.StatusBar.ClearAsync();

            if (result.Success)
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
        // Only show command for projects that can have output assemblies
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var project = await VS.Solutions.GetActiveProjectAsync();
            Command.Visible = project != null && IsSupportedProject(project);
        });
    }

    private static bool IsSupportedProject(Project project)
    {
        // Support C# and VB.NET projects
        var kind = project.GetType().Name;
        return kind.Contains("CSharp") || kind.Contains("VB") || kind.Contains("SDK");
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

            if (!string.IsNullOrEmpty(projectDir) && !string.IsNullOrEmpty(outputPath) && !string.IsNullOrEmpty(outputFileName))
            {
                var fullOutputPath = Path.Combine(projectDir, outputPath, outputFileName);
                if (File.Exists(fullOutputPath))
                {
                    return fullOutputPath;
                }
            }

            // Fallback: look for common output paths
            if (!string.IsNullOrEmpty(projectDir))
            {
                var projectName = project.Name;
                var possiblePaths = new[]
                {
                    Path.Combine(projectDir, "bin", "Release", "net10.0", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", "Release", "net9.0", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", "Release", "net8.0", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", "Debug", "net10.0", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", "Debug", "net9.0", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", "Debug", "net8.0", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", "Release", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", "Debug", $"{projectName}.dll"),
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            return null;
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
}
