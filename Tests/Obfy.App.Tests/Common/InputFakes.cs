using Obfy.App.Services;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services.Solution;
using CoreSession = Obfy.Core.Models.Solution.ProtectionSession;

namespace Obfy.App.Tests.Common;

/// <summary>In-memory preferences that count what the session writes and saves.</summary>
internal sealed class FakeSettings : ISettingsService
{
    private string? _lastOutputDirectory;

    public string? LastOutputDirectory
    {
        get => _lastOutputDirectory;
        set
        {
            _lastOutputDirectory = value;
            OutputAssignments.Add(value);
        }
    }

    public bool GenerateSymbolMap { get; set; }

    /// <summary>Every value <see cref="LastOutputDirectory"/> was assigned (not the starting one).</summary>
    public List<string?> OutputAssignments { get; } = [];

    public int Saves;

    public int Loads;

    /// <summary>What <see cref="LoadPreferencesAsync"/> puts into the properties, as a file on disk would.</summary>
    public Action? OnLoad { get; set; }

    public Task SavePreferencesAsync()
    {
        Interlocked.Increment(ref Saves);
        return Task.CompletedTask;
    }

    public Task LoadPreferencesAsync()
    {
        Loads++;
        OnLoad?.Invoke();
        return Task.CompletedTask;
    }

    public Task SaveSettingsAsync(ObfySettings settings, string filePath) => Task.CompletedTask;

    public Task<ObfySettings?> LoadSettingsAsync(string filePath) => Task.FromResult<ObfySettings?>(null);
}

internal sealed class FakeDialogs : IFileDialogs
{
    public string[] Files { get; set; } = [];
    public string? Folder { get; set; }
    public int FilePicks;
    public int FolderPicks;

    /// <summary>What the save dialog answers; null is a cancel.</summary>
    public string? SavePath { get; set; }
    public int SavePicks;

    public Task<string[]> PickFilesAsync(string title, string filter, bool multi)
    {
        FilePicks++;
        return Task.FromResult(Files);
    }

    public Task<string?> PickFolderAsync(string title)
    {
        FolderPicks++;
        return Task.FromResult(Folder);
    }

    public Task<string?> PickSaveAsync(string title, string filter, string? defaultName)
    {
        SavePicks++;
        return Task.FromResult(SavePath);
    }

    public Task<string?> PickKeyAsync() => Task.FromResult<string?>(null);
}

internal sealed class FakeEvents : IEventPublisher
{
    private readonly List<(string Name, object? Data)> _published = [];

    public IReadOnlyList<(string Name, object? Data)> Published
    {
        get
        {
            lock (_published)
                return _published.ToList();
        }
    }

    /// <summary>Runs inside every publish, on the publishing thread.</summary>
    public Action? OnPublish { get; set; }

    public void Publish(string eventName, object? data)
    {
        lock (_published)
            _published.Add((eventName, data));
        OnPublish?.Invoke();
    }
}

internal sealed class FakeShell : IShellLauncher
{
    public List<string> Opened { get; } = [];
    public List<string> Revealed { get; } = [];

    public void OpenFolder(string path) => Opened.Add(path);

    public void RevealFile(string path) => Revealed.Add(path);
}

/// <summary>Answers <see cref="Analyze"/> from a function, so a test chooses which rows a drop expands to.</summary>
internal sealed class FakeAnalyzer : ISolutionAnalyzer
{
    public Func<string, IReadOnlyList<ProjectProtectionEntry>> Entries { get; set; } = _ => [];

    public CoreSession Analyze(string path) => new() { SourcePath = path, Entries = Entries(path) };
}
