using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Models;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Shouldly;
using ProtectionSession = Obfy.App.Sessions.ProtectionSession;

namespace Obfy.App.Tests.Sessions;

/// <summary>
/// What a drop, the picker or the command line does with paths it cannot use (a notice, never silence), and which
/// <c>.json</c> files count as a configuration.
/// </summary>
public sealed class InputSessionDropTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"InputDrop_{Guid.NewGuid():N}");
    private readonly FakeSettings _settings = new();

    public InputSessionDropTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private InputSession Create(ILogger<InputSession>? logger = null) =>
        new(_settings, null, new FakeShell(), null, logger ?? NullLogger<InputSession>.Instance, TimeSpan.Zero);

    private string File_(string name, string? content = null)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content ?? "");
        return path;
    }

    // ---- Notices for what was skipped (finding 6) ----

    [Fact]
    public void AddPaths_UnsupportedFile_IsANoticeNamingIt()
    {
        var session = Create();

        session.AddPaths([File_("readme.txt"), File_("pic.png")]);

        session.GetSnapshot().Files.ShouldBeEmpty();
        var notice = session.GetSnapshot().Notices.ShouldHaveSingleItem();
        notice.Level.ShouldBe(NoticeLevel.Warning);
        notice.Message.ShouldContain("readme.txt");
        notice.Message.ShouldContain("pic.png");
    }

    [Fact]
    public void AddPaths_MissingFile_IsANoticeNamingIt()
    {
        var session = Create();

        session.AddPaths([Path.Combine(_root, "gone.dll")]);

        var notice = session.GetSnapshot().Notices.ShouldHaveSingleItem();
        notice.Level.ShouldBe(NoticeLevel.Warning);
        notice.Message.ShouldContain("gone.dll");
    }

    [Fact]
    public void AddPaths_Folder_IsANotice_NotSilentlyDropped()
    {
        var session = Create();
        var folder = Path.Combine(_root, "somefolder");
        Directory.CreateDirectory(folder);

        session.AddPaths([folder]);

        session.GetSnapshot().Files.ShouldBeEmpty();
        session.GetSnapshot().Notices.ShouldHaveSingleItem().Message.ShouldContain("somefolder");
    }

    [Fact]
    public void AddPaths_DuplicateOfAListedFile_IsANotice_AndTheListKeepsOneRow()
    {
        var session = Create();
        var dll = File_("app.dll");
        session.AddPaths([dll]);
        session.GetSnapshot().Notices.ShouldBeEmpty();

        session.AddPaths([dll]);

        session.GetSnapshot().Files.Count.ShouldBe(1);
        var notice = session.GetSnapshot().Notices.ShouldHaveSingleItem();
        notice.Level.ShouldBe(NoticeLevel.Info);
        notice.Message.ShouldContain("app.dll");
    }

    [Fact]
    public void AddPaths_ASkip_RaisesChangedSoThePageShowsTheNotice()
    {
        var session = Create();
        var raised = 0;
        session.Changed += () => raised++;

        session.AddPaths([File_("readme.txt")]);

        raised.ShouldBe(1);
    }

    [Fact]
    public void AddPaths_TheSamePathTwiceInOneDrop_IsOneRowAndANotice()
    {
        var session = Create();
        var dll = File_("app.dll");

        session.AddPaths([dll, dll]);

        session.GetSnapshot().Files.Count.ShouldBe(1);
        session.GetSnapshot().Notices.ShouldHaveSingleItem().Message.ShouldContain("app.dll");
    }

    [Fact]
    public void AddPaths_SeveralSkips_AreBatchedIntoOneNoticePerReason()
    {
        var session = Create();
        var dll = File_("app.dll");
        session.AddPaths([dll]);

        session.AddPaths([dll, File_("a.txt"), File_("b.txt"), Path.Combine(_root, "x.dll"), Path.Combine(_root, "y.dll")]);

        var notices = session.GetSnapshot().Notices;
        notices.Count.ShouldBe(3);
        notices.Count(n => n.Message.Contains("a.txt") && n.Message.Contains("b.txt")).ShouldBe(1);
        notices.Count(n => n.Message.Contains("x.dll") && n.Message.Contains("y.dll")).ShouldBe(1);
    }

    [Fact]
    public void AddPaths_AGoodDrop_HasNoNotice()
    {
        var session = Create();

        session.AddPaths([File_("a.dll"), File_("b.exe"), File_("c.cs")]);

        session.GetSnapshot().Notices.ShouldBeEmpty();
        session.GetSnapshot().Files.Count.ShouldBe(3);
    }

    [Fact]
    public void AddPaths_Skips_AreLoggedAsCountsOnly()
    {
        var logger = new CapturingLogger();
        var session = Create(logger);
        var dll = File_("secret-project-name.dll");
        session.AddPaths([dll]);

        session.AddPaths([dll, File_("private-notes.txt"), Path.Combine(_root, "missing-customer.dll")]);

        logger.Lines.ShouldNotBeEmpty();
        logger.Lines.ShouldAllBe(l => !l.Contains("secret-project-name") && !l.Contains("private-notes")
            && !l.Contains("missing-customer") && !l.Contains(_root));
    }

    // ---- Which .json is a configuration (finding 3) ----

    [Theory]
    [InlineData("MyApp.deps.json", """{ "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" }, "targets": {}, "libraries": {} }""")]
    [InlineData("MyApp.runtimeconfig.json", """{ "runtimeOptions": { "tfm": "net8.0" } }""")]
    [InlineData("launchSettings.json", """{ "profiles": { "App": {} } }""")]
    [InlineData("array.json", "[1, 2, 3]")]
    [InlineData("scalar.json", "42")]
    [InlineData("null.json", "null")]
    [InlineData("empty-object.json", "{}")]
    public void AddPaths_AJsonWithNoObfySettings_IsNotAConfiguration_AndIsSaidSo(string name, string content)
    {
        var session = Create();
        var path = File_(name, content);

        var result = session.AddPaths([path]);

        result.ConfigPath.ShouldBeNull();
        session.GetSnapshot().Files.ShouldBeEmpty();
        var notice = session.GetSnapshot().Notices.ShouldHaveSingleItem();
        notice.Level.ShouldBe(NoticeLevel.Warning);
        notice.Message.ShouldContain(name);
        InputSession.IsConfigPath(path).ShouldBeFalse();
        InputSession.CanAcceptDrop([path]).ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{ "level": "Aggressive" }""")]
    [InlineData("""{ "LEVEL": 2 }""")]
    [InlineData("""{ "stringEncryption": { "enabled": false } }""")]
    [InlineData("""{ "Signing": { "enabled": false }, "unrelated": 1 }""")]
    [InlineData("""{ "runtimeProfile": "Default" }""")]
    public void AddPaths_AJsonWithAnObfySettingsKey_IsTheConfiguration(string content)
    {
        var session = Create();
        var path = File_("obfy.json", content);

        var result = session.AddPaths([path]);

        result.ConfigPath.ShouldBe(path);
        session.GetSnapshot().Notices.ShouldBeEmpty();
        InputSession.IsConfigPath(path).ShouldBeTrue();
        InputSession.CanAcceptDrop([path]).ShouldBeTrue();
    }

    [Fact]
    public void AddPaths_AConfigurationSavedWithAByteOrderMark_IsTheConfiguration()
    {
        // Notepad and some editors write a BOM; the loader reads it, so the check must too.
        var session = Create();
        var path = Path.Combine(_root, "bom.json");
        File.WriteAllText(path, """{ "level": "Minimal" }""", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        session.AddPaths([path]).ConfigPath.ShouldBe(path);
        session.GetSnapshot().Notices.ShouldBeEmpty();
    }

    [Fact]
    public void AddPaths_AHugeJson_IsNotReadAsAConfiguration()
    {
        var session = Create();
        var path = Path.Combine(_root, "huge.json");
        using (var stream = File.Create(path))
            stream.SetLength(17 * 1024 * 1024);

        session.AddPaths([path]).ConfigPath.ShouldBeNull();
        session.GetSnapshot().Notices.ShouldHaveSingleItem().Message.ShouldContain("huge.json");
    }

    [Fact]
    public void AddPaths_EverySettingsPropertyNameIsRecognised()
    {
        // The recognised keys follow ObfySettings, so a new setting needs no second list.
        var session = Create();
        var properties = typeof(ObfySettings).GetProperties()
            .Where(p => p.CanWrite && !p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Any())
            .Select(p => p.Name)
            .ToList();
        properties.ShouldContain("Level");
        properties.ShouldContain("Signing");

        foreach (var property in properties)
        {
            var camel = char.ToLowerInvariant(property[0]) + property[1..];
            var path = File_($"{property}.json", $$"""{ "{{camel}}": null }""");
            InputSession.IsConfigPath(path).ShouldBeTrue(property);
        }
    }

    [Fact]
    public async Task Intake_DroppingADepsJson_DoesNotAdoptItAsTheSaveTarget()
    {
        var input = Create();
        var protection = new ProtectionSession();
        var intake = new InputIntake(input, protection, IdleRun.For(input, protection), NullLogger<InputIntake>.Instance);
        var deps = File_("MyApp.deps.json", """{ "runtimeTarget": {}, "targets": {}, "libraries": {} }""");

        await intake.AddPathsAsync([deps], default);

        protection.ConfigPath.ShouldBeNull();
        input.GetSnapshot().Notices.ShouldHaveSingleItem().Message.ShouldContain("MyApp.deps.json");
    }

    [Fact]
    public async Task Startup_ADepsJsonArgument_IsNotLoadedAsAConfiguration()
    {
        var input = Create();
        var protection = new ProtectionSession();
        var intake = new InputIntake(input, protection, IdleRun.For(input, protection), NullLogger<InputIntake>.Instance);
        var deps = File_("MyApp.deps.json", """{ "targets": {} }""");
        var dll = File_("MyApp.dll");

        await intake.ApplyStartupAsync([deps, dll]);

        protection.ConfigPath.ShouldBeNull();
        input.GetSnapshot().Files.Single().Path.ShouldBe(dll);
        input.GetSnapshot().Notices.ShouldHaveSingleItem().Message.ShouldContain("MyApp.deps.json");
    }

    [Fact]
    public void AddPaths_AJsonThatCannotBeParsed_StillGoesToTheLoader_SoTheUserGetsItsError()
    {
        // The loader fails on it and adopts nothing, and says why; a notice here would hide that reason.
        var session = Create();
        var path = File_("broken.json", "{ this is not json");

        session.AddPaths([path]).ConfigPath.ShouldBe(path);
    }

    private sealed class CapturingLogger : ILogger<InputSession>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
            if (exception is not null)
                Lines.Add(exception.ToString());
        }
    }
}
