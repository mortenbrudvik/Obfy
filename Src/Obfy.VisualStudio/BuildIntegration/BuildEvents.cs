using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Obfy.VisualStudio.Services;

namespace Obfy.VisualStudio.BuildIntegration;

/// <summary>
/// Handles post-build obfuscation for projects with post-build enabled
/// </summary>
public static class BuildEvents
{
    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        VS.Events.BuildEvents.ProjectBuildDone += OnProjectBuildDone;
    }

    private static void OnProjectBuildDone(ProjectBuildDoneEventArgs e)
    {
        if (e.Project == null || !e.IsSuccessful)
        {
            return;
        }

        // Run the post-build check asynchronously
        _ = HandlePostBuildAsync(e.Project);
    }

    private static async Task HandlePostBuildAsync(Project project)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var output = ObfyPackage.OutputService;
            var obfuscator = ObfyPackage.ObfuscationService;
            var settingsService = ObfyPackage.ProjectSettingsService;

            if (settingsService == null || obfuscator == null)
            {
                return;
            }

            // Check if post-build is enabled for this project
            var settings = await settingsService.LoadSettingsAsync(project);
            if (settings == null || !settings.PostBuildEnabled)
            {
                return;
            }

            // Check if we should only run for Release builds
            var options = ObfyPackage.Options;
            if (options?.ReleaseOnly ?? true)
            {
                var configuration = await GetActiveConfigurationAsync(project);
                if (!string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase))
                {
                    output?.Info($"Skipping post-build obfuscation for {project.Name} ({configuration} configuration)");
                    return;
                }
            }

            // Get the output assembly path
            var assemblyPath = await GetOutputAssemblyPathAsync(project);
            if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
            {
                output?.Warning($"Could not find output assembly for {project.Name}");
                return;
            }

            var projectDir = Path.GetDirectoryName(await project.GetAttributeAsync("FullPath"));

            // ProjectBuildDone also fires for projects that were already up to date (nothing was built) and for
            // other configurations. If the output is the file this step obfuscated last time, building produced
            // no new assembly: obfuscating it again would protect an already protected file a second time.
            if (!string.IsNullOrEmpty(projectDir) && PostBuildStamp.IsAlreadyObfuscated(projectDir!, assemblyPath!))
            {
                output?.Info($"Skipping post-build obfuscation for {project.Name}: the output was not rebuilt since it was last obfuscated");
                return;
            }

            // Show output window if configured
            if (options?.ShowOutputWindow ?? true)
            {
                await output?.ActivateAsync()!;
            }

            output?.Info($"Post-build obfuscation starting for {project.Name}...");

            // Run obfuscation
            await VS.StatusBar.ShowProgressAsync("Post-build obfuscation...", 1, 2);

            var cts = new CancellationTokenSource();
            var configPath = !string.IsNullOrEmpty(projectDir) && settingsService is not null
                ? settingsService.GetSettingsFilePath(projectDir)
                : null;
            if (configPath is not null && !File.Exists(configPath))
                configPath = null;
            var result = await obfuscator.ObfuscateAsync(assemblyPath!, assemblyPath, settings, cts.Token, configPath);

            await VS.StatusBar.ClearAsync();

            if (result.Success)
            {
                if (!string.IsNullOrEmpty(projectDir))
                    PostBuildStamp.Record(projectDir!, assemblyPath!);

                output?.Success($"Post-build obfuscation complete for {project.Name}: {result.Statistics.TotalTransformations} transformations");
            }
            else
            {
                output?.Error($"Post-build obfuscation failed for {project.Name}: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            ObfyPackage.OutputService?.Error($"Post-build obfuscation error: {ex.Message}");
        }
    }

    private static async Task<string?> GetActiveConfigurationAsync(Project project)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        try
        {
            return await project.GetAttributeAsync("Configuration");
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetOutputAssemblyPathAsync(Project project)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        try
        {
            var outputPath = await project.GetAttributeAsync("OutputPath");
            var outputFileName = await project.GetAttributeAsync("OutputFileName");
            var projectPath = await project.GetAttributeAsync("FullPath");
            var projectDir = Path.GetDirectoryName(projectPath);

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
