using System;
using System.IO;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Service for managing per-project obfy.json settings files
/// </summary>
public class ProjectSettingsService : IProjectSettingsService
{
    private const string SettingsFileName = "obfy.json";

    public async Task<ObfySettings?> LoadSettingsAsync(Project project)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var projectDir = await GetProjectDirectoryAsync(project);
        if (string.IsNullOrEmpty(projectDir))
        {
            return null;
        }

        return await LoadSettingsFromDirectoryAsync(projectDir!);
    }

    public async Task<ObfySettings?> LoadSettingsFromDirectoryAsync(string projectDirectory)
    {
        var filePath = GetSettingsFilePath(projectDirectory);

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await ReadFileAsync(filePath);
            return ObfySettingsJson.Parse(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse {filePath}: {ex.Message}");
            throw;
        }
    }

    public async Task SaveSettingsAsync(Project project, ObfySettings settings)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var projectDir = await GetProjectDirectoryAsync(project);
        if (!string.IsNullOrEmpty(projectDir))
        {
            await SaveSettingsToDirectoryAsync(projectDir!, settings);
        }
    }

    public async Task SaveSettingsToDirectoryAsync(string projectDirectory, ObfySettings settings)
    {
        var filePath = GetSettingsFilePath(projectDirectory);
        var json = ObfySettingsJson.Serialize(settings);
        await WriteFileAsync(filePath, json);
    }

    public async Task<bool> HasSettingsAsync(Project project)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var projectDir = await GetProjectDirectoryAsync(project);
        if (string.IsNullOrEmpty(projectDir))
        {
            return false;
        }

        var filePath = GetSettingsFilePath(projectDir!);
        return File.Exists(filePath);
    }

    public string GetSettingsFilePath(string projectDirectory)
    {
        return Path.Combine(projectDirectory, SettingsFileName);
    }

    public async Task<bool> IsPostBuildEnabledAsync(Project project)
    {
        var settings = await LoadSettingsAsync(project);
        return settings?.PostBuildEnabled ?? false;
    }

    public async Task SetPostBuildEnabledAsync(Project project, bool enabled)
    {
        var settings = await LoadSettingsAsync(project);

        if (settings == null)
        {
            // Create new settings with defaults
            var options = ObfyPackage.Options;
            var level = options?.DefaultLevel ?? ObfuscationLevel.Standard;
            settings = ObfySettings.ForLevel(level);
        }

        settings.PostBuildEnabled = enabled;
        await SaveSettingsAsync(project, settings);
    }

    private static async Task<string?> GetProjectDirectoryAsync(Project project)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var fullPath = await project.GetAttributeAsync("FullPath");
        if (string.IsNullOrEmpty(fullPath))
        {
            return null;
        }

        return Path.GetDirectoryName(fullPath);
    }

    private static Task<string> ReadFileAsync(string path)
    {
        return Task.Run(() => File.ReadAllText(path));
    }

    private static Task WriteFileAsync(string path, string content)
    {
        return Task.Run(() => File.WriteAllText(path, content));
    }
}
