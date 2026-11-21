using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Obfy.App.Services;
using Shouldly;

namespace Obfy.App.Tests.Services;

/// <summary>
/// Loading and saving <c>ui-preferences.json</c>: a file that cannot be read or written is a warning and never an
/// exception (the start-up state depends on it), and the warning names no path (it holds the Windows profile name).
/// </summary>
public sealed class SettingsServicePreferencesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"SettingsPrefs_{Guid.NewGuid():N}");
    private readonly string _prefs;
    private readonly CapturingLogger _logger = new();

    public SettingsServicePreferencesTests()
    {
        Directory.CreateDirectory(_dir);
        _prefs = Path.Combine(_dir, "profile-name-jane", "ui-preferences.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_prefs)!);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private SettingsService Create() => new(_logger, _prefs);

    private void AssertLoggedOnlyTheType(string exceptionType)
    {
        var warning = _logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Exception.ShouldBeNull("the exception object carries its message, which can hold the path");
        warning.Message.ShouldContain(exceptionType);
        warning.Message.ShouldNotContain("profile-name-jane");
        warning.Message.ShouldNotContain(_dir);
    }

    [Fact]
    public async Task LoadPreferences_RoundTripsWhatSaveWrote()
    {
        var first = Create();
        first.LastOutputDirectory = @"C:\out";
        first.GenerateSymbolMap = true;
        await first.SavePreferencesAsync();

        var second = Create();
        await second.LoadPreferencesAsync();

        second.LastOutputDirectory.ShouldBe(@"C:\out");
        second.GenerateSymbolMap.ShouldBeTrue();
        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task LoadPreferences_AFileTheUserCannotRead_IsAWarning_NotAnException()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await File.WriteAllTextAsync(_prefs, """{ "lastOutputDirectory": "C:\\out", "generateSymbolMap": true }""");
        var info = new FileInfo(_prefs);
        var security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny);
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        try
        {
            var service = Create();

            await Should.NotThrowAsync(service.LoadPreferencesAsync);

            service.LastOutputDirectory.ShouldBeNull();
            service.GenerateSymbolMap.ShouldBeFalse();
            AssertLoggedOnlyTheType(nameof(UnauthorizedAccessException));
        }
        finally
        {
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public async Task LoadPreferences_ALockedFile_LogsTheTypeOnly()
    {
        await File.WriteAllTextAsync(_prefs, "{}");
        using var hold = new FileStream(_prefs, FileMode.Open, FileAccess.Read, FileShare.None);

        await Should.NotThrowAsync(Create().LoadPreferencesAsync);

        AssertLoggedOnlyTheType(nameof(IOException));
    }

    [Fact]
    public async Task LoadPreferences_CorruptJson_LogsTheTypeOnly()
    {
        await File.WriteAllTextAsync(_prefs, "{ nope");

        await Should.NotThrowAsync(Create().LoadPreferencesAsync);

        AssertLoggedOnlyTheType("JsonException");
    }

    [Fact]
    public async Task SavePreferences_WhenTheTempFileCannotBeWritten_LogsTheTypeOnly()
    {
        // A folder where the temp file should go: the write is refused.
        Directory.CreateDirectory(_prefs + ".tmp");
        var service = Create();
        service.LastOutputDirectory = @"C:\out";

        await Should.NotThrowAsync(service.SavePreferencesAsync);

        _logger.Entries.ShouldHaveSingleItem().Exception.ShouldBeNull();
        _logger.Entries.Single().Message.ShouldNotContain("profile-name-jane");
        _logger.Entries.Single().Message.ShouldNotContain(_dir);
    }

    private sealed class CapturingLogger : ILogger<SettingsService>
    {
        public record Entry(LogLevel Level, string Message, Exception? Exception);

        private readonly List<Entry> _entries = [];

        public IReadOnlyList<Entry> Entries
        {
            get
            {
                lock (_entries)
                    return _entries.ToList();
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
                _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }
    }
}
