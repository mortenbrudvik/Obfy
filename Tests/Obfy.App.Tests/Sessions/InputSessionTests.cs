using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App;
using Obfy.App.Features.Files;
using Obfy.App.Features.Output;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services.Solution;
using Shouldly;
using ProtectionSession = Obfy.App.Sessions.ProtectionSession;

namespace Obfy.App.Tests.Sessions;

/// <summary>
/// The WPF <c>FilesViewModelTests</c> ported to <see cref="InputSession"/> (43 tests: commands became session methods
/// and the handlers behind <c>files.pick</c> / <c>output.pickFolder</c>, property-change checks became snapshot checks),
/// plus the tests for the three defects fixed in the lift (UI-053, UI-054, UI-058), the real drop handler, and the
/// thread rules for startup.
/// </summary>
public sealed class InputSessionTests : IDisposable
{
    // A complete Aggressive configuration: a file whose level is a preset but whose switches are not that preset's loads as Custom.
    private static readonly string AggressiveConfigJson =
        Obfy.Common.JsonUtils.Serialize(ObfySettings.ForLevel(ObfuscationLevel.Aggressive));

    private readonly FakeSettings _settings = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeShell _shell = new();
    private readonly string _tempDirectory;

    public InputSessionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"InputSessionTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    // A zero delay saves at once, so a test sees the save the change caused.
    private InputSession Create(
        ISettingsService? settings = null, ISolutionAnalyzer? analyzer = null, IEventPublisher? events = null,
        TimeSpan? delay = null) =>
        new(settings ?? _settings, analyzer, _shell, events, NullLogger<InputSession>.Instance, delay ?? TimeSpan.Zero);

    private InputIntake Intake(InputSession input, ProtectionSession? protection = null)
    {
        protection ??= new ProtectionSession();
        return new(input, protection, IdleRun.For(input, protection), NullLogger<InputIntake>.Instance);
    }

    private string CreateTestFile(string name, string? subDirectory = null)
    {
        var directory = subDirectory is null ? _tempDirectory : Path.Combine(_tempDirectory, subDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    private static FileRow Only(InputSession session) => session.GetSnapshot().Files.Single();

    private PickOutputFolderRequestHandler PickFolderHandler(InputSession input) =>
        new(input, _dialogs, IdleRun.For(input, new ProtectionSession()));

    private PickSymbolMapRequestHandler PickMapHandler(InputSession input) =>
        new(input, _dialogs, IdleRun.For(input, new ProtectionSession()));

    private string WriteAppAndTestsSolution()
    {
        var appProj = Path.Combine(_tempDirectory, "App", "App.csproj");
        var testsProj = Path.Combine(_tempDirectory, "App.Tests", "App.Tests.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(appProj)!);
        Directory.CreateDirectory(Path.GetDirectoryName(testsProj)!);
        File.WriteAllText(appProj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(testsProj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
            </Project>
            """);

        var appDll = Path.Combine(_tempDirectory, "App", "bin", "Release", "net8.0", "App.dll");
        var testsDll = Path.Combine(_tempDirectory, "App.Tests", "bin", "Release", "net8.0", "App.Tests.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(appDll)!);
        Directory.CreateDirectory(Path.GetDirectoryName(testsDll)!);
        File.WriteAllBytes(appDll, []);
        File.WriteAllBytes(testsDll, []);

        var sln = Path.Combine(_tempDirectory, "App.sln");
        var appGuid = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var testsGuid = Guid.NewGuid().ToString("D").ToUpperInvariant();
        File.WriteAllText(sln, string.Join(Environment.NewLine,
        [
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"App\", \"App\\App.csproj\", \"{" + appGuid + "}\"",
            "EndProject",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"App.Tests\", \"App.Tests\\App.Tests.csproj\", \"{" + testsGuid + "}\"",
            "EndProject"
        ]));
        return sln;
    }

    private string WriteTestsOnlySolution()
    {
        var testsProj = Path.Combine(_tempDirectory, "Foo.Tests", "Foo.Tests.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(testsProj)!);
        File.WriteAllText(testsProj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
            </Project>
            """);

        var sln = Path.Combine(_tempDirectory, "Foo.sln");
        var guid = Guid.NewGuid().ToString("D").ToUpperInvariant();
        File.WriteAllText(sln, string.Join(Environment.NewLine,
        [
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Foo.Tests\", \"Foo.Tests\\Foo.Tests.csproj\", \"{" + guid + "}\"",
            "EndProject"
        ]));
        return sln;
    }

    /// <summary>An analyzer that answers every project drop with one skipped row, as a test project would be.</summary>
    private FakeAnalyzer SkippedTestProjectAnalyzer(string reason = "Test project") => new()
    {
        Entries = path =>
        [
            ProjectProtectionEntry.Skipped(path, "App.Tests", ProjectSkipReason.Test, reason)
        ]
    };

    #region Initial State Tests

    [Fact]
    public void Constructor_InitializesEmptyFileList()
    {
        Create().GetSnapshot().Files.Count.ShouldBe(0);
    }

    [Fact]
    public void Constructor_InitializesHasFilesToFalse()
    {
        var snapshot = Create().GetSnapshot();

        snapshot.Files.Any().ShouldBeFalse();
        snapshot.HasIncludedFiles.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_InitializesHasNoFilesToTrue()
    {
        var snapshot = Create().GetSnapshot();

        snapshot.Files.ShouldBeEmpty();
        snapshot.OutputHint.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_LoadsLastOutputDirectoryFromSettings()
    {
        var output = Path.Combine(_tempDirectory, "saved-output");
        Directory.CreateDirectory(output);
        _settings.LastOutputDirectory = output;

        var snapshot = Create().GetSnapshot();

        snapshot.OutputFolder.ShouldBe(output);
        snapshot.OutputWarning.ShouldBeNull();
    }

    [Fact]
    public void Constructor_MissingSavedOutputDirectory_ClearsPathAndWarns()
    {
        var missing = Path.Combine(_tempDirectory, "missing-output");
        _settings.LastOutputDirectory = missing;

        var snapshot = Create().GetSnapshot();

        snapshot.OutputFolder.ShouldBeEmpty();
        snapshot.OutputWarning.ShouldNotBeNull().ShouldContain(missing);
    }

    [Fact]
    public void Constructor_LoadsGenerateSymbolMapFromSettings()
    {
        _settings.GenerateSymbolMap = true;

        Create().GetSnapshot().WriteSymbolMap.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreferences_CopiesLoadedValuesFromSettings()
    {
        // The constructor snapshots empty values; ApplyPreferences is the fix after the file has been read.
        var session = Create();
        session.GetSnapshot().OutputFolder.ShouldBe(string.Empty);
        session.GetSnapshot().WriteSymbolMap.ShouldBeFalse();

        var loaded = Path.Combine(_tempDirectory, "loaded-output");
        Directory.CreateDirectory(loaded);
        _settings.LastOutputDirectory = loaded;
        _settings.GenerateSymbolMap = true;
        _settings.OutputAssignments.Clear();

        session.ApplyPreferences();

        var snapshot = session.GetSnapshot();
        snapshot.OutputFolder.ShouldBe(loaded);
        snapshot.WriteSymbolMap.ShouldBeTrue();
        _settings.Saves.ShouldBe(0);
        _settings.OutputAssignments.ShouldBeEmpty();
    }

    [Fact]
    public void TypingOutputDirectory_SavesPreferences()
    {
        Create().SetOutputFolder(@"C:\Typed\Output");

        _settings.OutputAssignments.ShouldBe([@"C:\Typed\Output"]);
        _settings.Saves.ShouldBe(1);
    }

    [Fact]
    public void TogglingSymbolMap_SavesPreferences()
    {
        Create().SetWriteSymbolMap(true);

        _settings.GenerateSymbolMap.ShouldBeTrue();
        _settings.Saves.ShouldBe(1);
    }

    #endregion

    #region Drop Tests

    [Fact]
    public void HandleFileDrop_WithValidDll_AddsFile()
    {
        var session = Create();
        var dllPath = CreateTestFile("test.dll");

        session.AddPaths([dllPath]);

        Only(session).Path.ShouldBe(dllPath);
    }

    [Fact]
    public void ApplyStartup_AddsFilesAndOutputDirectory()
    {
        var session = Create();
        var dllPath = CreateTestFile("startup.dll");
        var output = Path.Combine(_tempDirectory, "out");

        session.ApplyStartup([dllPath, "-o", output]);

        var snapshot = session.GetSnapshot();
        snapshot.Files.Single().Path.ShouldBe(dllPath);
        snapshot.OutputFolder.ShouldBe(output);
    }

    [Fact]
    public async Task ApplyStartup_DoesNotPersistSessionOutputDirectory()
    {
        var session = Create();
        var output = Path.Combine(_tempDirectory, "session-out");
        Directory.CreateDirectory(output);
        session.ApplyStartup(["-o", output]);

        await session.PersistPreferencesAsync();

        _settings.OutputAssignments.ShouldBeEmpty();

        var edited = Path.Combine(_tempDirectory, "edited-out");
        session.SetOutputFolder(edited);
        _settings.OutputAssignments.ShouldBe([edited]);
    }

    [Fact]
    public void HandleFileDrop_WithValidExe_AddsFile()
    {
        var session = Create();

        session.AddPaths([CreateTestFile("test.exe")]);

        session.GetSnapshot().Files.Count.ShouldBe(1);
    }

    [Fact]
    public void HandleFileDrop_WithCsFile_AddsFile()
    {
        var session = Create();

        session.AddPaths([CreateTestFile("Program.cs")]);

        Only(session).Kind.ShouldBe(FileKind.Source);
    }

    [Fact]
    public void HandleFileDrop_WithInvalidExtension_IgnoresFile()
    {
        var session = Create();

        session.AddPaths([CreateTestFile("readme.txt")]);

        session.GetSnapshot().Files.Count.ShouldBe(0);
    }

    [Fact]
    public void HandleFileDrop_WithNonExistentFile_IgnoresFile()
    {
        var session = Create();

        session.AddPaths([Path.Combine(_tempDirectory, "nonexistent.dll")]);

        session.GetSnapshot().Files.Count.ShouldBe(0);
    }

    [Fact]
    public void HandleFileDrop_WithDuplicateFile_IgnoresDuplicate()
    {
        var session = Create();
        var dllPath = CreateTestFile("test.dll");
        session.AddPaths([dllPath]);

        session.AddPaths([dllPath]);

        session.GetSnapshot().Files.Count.ShouldBe(1);
    }

    [Fact]
    public void HandleFileDrop_WithMultipleFiles_AddsAll()
    {
        var session = Create();

        session.AddPaths([CreateTestFile("test1.dll"), CreateTestFile("test2.dll"), CreateTestFile("app.exe")]);

        session.GetSnapshot().Files.Count.ShouldBe(3);
    }

    [Fact]
    public void HandleFileDrop_UpdatesHasFiles()
    {
        var session = Create();
        session.GetSnapshot().Files.Any().ShouldBeFalse();

        session.AddPaths([CreateTestFile("test.dll")]);

        var snapshot = session.GetSnapshot();
        snapshot.Files.Any().ShouldBeTrue();
        snapshot.HasIncludedFiles.ShouldBeTrue();
    }

    #endregion

    [Fact]
    public void CanAcceptDrop_TrueWhenAnySupportedFile()
    {
        var dll = CreateTestFile("ok.dll");
        InputSession.CanAcceptDrop([dll, CreateTestFile("no.txt")]).ShouldBeTrue();
        InputSession.CanAcceptDrop([CreateTestFile("no.txt")]).ShouldBeFalse();
        InputSession.CanAcceptDrop(null).ShouldBeFalse();
        InputSession.IsSupportedInputPath(":::not-a-path").ShouldBeFalse();
    }

    [Fact]
    public void IsSupportedInputPath_TrueForTempSln()
    {
        InputSession.IsSupportedInputPath(CreateTestFile("App.sln")).ShouldBeTrue();
    }

    [Fact]
    public void CanAcceptDrop_TrueForSln()
    {
        InputSession.CanAcceptDrop([CreateTestFile("App.sln")]).ShouldBeTrue();
    }

    [Fact]
    public void HasIncludedFiles_FalseWhenOnlySkippedRows()
    {
        var session = Create(analyzer: SkippedTestProjectAnalyzer());

        session.AddPaths([CreateTestFile("skipped.csproj")]);

        var snapshot = session.GetSnapshot();
        snapshot.HasIncludedFiles.ShouldBeFalse();
        snapshot.Files.Any().ShouldBeTrue();
    }

    [Fact]
    public void FromSessionEntry_UsesSkipMessageOrFallsBackToSkipReason()
    {
        var analyzer = new FakeAnalyzer
        {
            Entries = path => Path.GetFileName(path) == "Native.sln"
                ? [new ProjectProtectionEntry
                {
                    ProjectPath = Path.Combine(_tempDirectory, "Native.vcxproj"),
                    ProjectName = "Native",
                    SkipReason = ProjectSkipReason.Unsupported,
                    SkipMessage = "Unsupported project type '.vcxproj'"
                }]
                : [new ProjectProtectionEntry
                {
                    ProjectPath = Path.Combine(_tempDirectory, "Ghost.csproj"),
                    ProjectName = "Ghost",
                    SkipReason = ProjectSkipReason.MissingProject
                }]
        };
        var session = Create(analyzer: analyzer);

        session.AddPaths([CreateTestFile("Native.sln"), CreateTestFile("Ghost.sln")]);

        var rows = session.GetSnapshot().Files;
        var withMessage = rows.Single(r => r.Name == "Native.vcxproj");
        withMessage.Status.ShouldBe(FileStatus.Skipped);
        withMessage.Origin.ShouldBe(FileOrigin.Solution);
        withMessage.SkipReason.ShouldBe("Unsupported project type '.vcxproj'");

        var withoutMessage = rows.Single(r => r.Name == "Ghost.csproj");
        withoutMessage.Status.ShouldBe(FileStatus.Skipped);
        withoutMessage.SkipReason.ShouldBe(nameof(ProjectSkipReason.MissingProject));
    }

    [Fact]
    public void HandleFileDrop_WithSolution_AddsIncludedDllAndSkippedTest()
    {
        var sln = WriteAppAndTestsSolution();
        var session = Create(analyzer: new SolutionAnalyzer());

        session.AddPaths([sln]);

        var rows = session.GetSnapshot().Files;
        rows.Count.ShouldBe(2);

        var app = rows.Single(f => f.Name == "App.dll");
        app.Included.ShouldBeTrue();
        app.Status.ShouldBe(FileStatus.Pending);
        app.Origin.ShouldBe(FileOrigin.Solution);
        app.Kind.ShouldBe(FileKind.Assembly);
        app.Path.ShouldEndWith(Path.Combine("App", "bin", "Release", "net8.0", "App.dll"));
        session.IncludedInputs().Single().Hints.ShouldNotBeNull();

        var tests = rows.Single(f => f.Status == FileStatus.Skipped);
        tests.Name.ShouldBe("App.Tests.csproj");
        tests.Included.ShouldBeFalse();
        tests.SkipReason.ShouldBe("Test project");
        tests.Origin.ShouldBe(FileOrigin.Solution);
    }

    [Fact]
    public void HandleFileDrop_SkipOnlySolution_ShowsWarning()
    {
        var sln = WriteTestsOnlySolution();
        var session = Create(analyzer: new SolutionAnalyzer());

        session.AddPaths([sln]);

        var snapshot = session.GetSnapshot();
        snapshot.HasIncludedFiles.ShouldBeFalse();
        var notice = snapshot.Notices.ShouldHaveSingleItem();
        notice.Title.ShouldBe("Nothing to protect");
        notice.Level.ShouldBe(NoticeLevel.Warning);
        notice.Message.ShouldContain("No built outputs");
    }

    [Fact]
    public void HandleFileDrop_CorruptProject_ShowsErrorAndDoesNotThrow()
    {
        var csproj = Path.Combine(_tempDirectory, "Broken.csproj");
        File.WriteAllText(csproj, "<not xml");
        var session = Create(analyzer: new SolutionAnalyzer());

        Should.NotThrow(() => session.AddPaths([csproj]));

        var row = Only(session);
        row.Status.ShouldBe(FileStatus.Skipped);
        row.SkipReason!.ShouldContain("Failed to read project");
    }

    [Fact]
    public void ResetFileStatus_LeavesSkippedRowsSkipped()
    {
        var session = Create(analyzer: SkippedTestProjectAnalyzer());
        var dll = CreateTestFile("app.dll");
        session.AddPaths([dll, CreateTestFile("App.Tests.csproj")]);
        var dllRow = session.GetSnapshot().Files.Single(f => f.Name == "app.dll");
        session.SetFileResult(dllRow.Id, FileStatus.Success);

        session.ResetStatus();

        var rows = session.GetSnapshot().Files;
        rows.Single(f => f.Name == "app.dll").Status.ShouldBe(FileStatus.Pending);
        rows.ShouldContain(f => f.Name == "App.Tests.csproj" && f.Status == FileStatus.Skipped);
    }

    [Fact]
    public void HandleFileDrop_WithInvalidPath_DoesNotThrow()
    {
        var session = Create();

        Should.NotThrow(() => session.AddPaths(["\0invalid", "", @"C:\nope\missing.dll"]));

        session.GetSnapshot().Files.Count.ShouldBe(0);
    }

    [Fact]
    public async Task AddFilesCommand_AddsCsFiles()
    {
        var session = Create();
        _dialogs.Files = [CreateTestFile("Program.cs")];

        await new PickFilesRequestHandler(Intake(session), _dialogs, session, IdleRun.For(session, new ProtectionSession())).Handle(new PickFilesRequest(), default);

        Only(session).Kind.ShouldBe(FileKind.Source);
    }

    #region Pick Tests

    [Fact]
    public async Task AddFilesCommand_CallsFileDialogService()
    {
        var session = Create();

        await new PickFilesRequestHandler(Intake(session), _dialogs, session, IdleRun.For(session, new ProtectionSession())).Handle(new PickFilesRequest(), default);

        _dialogs.FilePicks.ShouldBe(1);
    }

    [Fact]
    public async Task AddFilesCommand_AddsSelectedFiles()
    {
        var session = Create();
        _dialogs.Files = [CreateTestFile("selected1.dll"), CreateTestFile("selected2.dll")];

        var snapshot = await new PickFilesRequestHandler(Intake(session), _dialogs, session, IdleRun.For(session, new ProtectionSession())).Handle(new PickFilesRequest(), default);

        snapshot.Files.Count.ShouldBe(2);
    }

    #endregion

    #region Remove Tests

    [Fact]
    public void RemoveFileCommand_RemovesFile()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("test.dll")]);

        session.Remove(Only(session).Id);

        session.GetSnapshot().Files.Count.ShouldBe(0);
    }

    [Fact]
    public void RemoveFileCommand_WithNull_DoesNothing()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("test.dll")]);

        session.Remove("no-such-id");

        session.GetSnapshot().Files.Count.ShouldBe(1);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void ClearFilesCommand_ClearsAllFiles()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("test1.dll"), CreateTestFile("test2.dll")]);

        session.Clear();

        var snapshot = session.GetSnapshot();
        snapshot.Files.Count.ShouldBe(0);
        snapshot.Files.Any().ShouldBeFalse();
    }

    #endregion

    #region Browse Output Tests

    [Fact]
    public async Task BrowseOutputDirectoryCommand_CallsFileDialogService()
    {
        await PickFolderHandler(Create()).Handle(new PickOutputFolderRequest(), default);

        _dialogs.FolderPicks.ShouldBe(1);
    }

    [Fact]
    public async Task BrowseOutputDirectoryCommand_SetsOutputDirectory()
    {
        _dialogs.Folder = @"C:\Selected\Output";

        var snapshot = await PickFolderHandler(Create()).Handle(new PickOutputFolderRequest(), default);

        snapshot.OutputFolder.ShouldBe(@"C:\Selected\Output");
    }

    [Fact]
    public async Task BrowseOutputDirectoryCommand_SavesToSettings()
    {
        _dialogs.Folder = @"C:\Selected\Output";

        await PickFolderHandler(Create()).Handle(new PickOutputFolderRequest(), default);

        _settings.OutputAssignments.ShouldBe([@"C:\Selected\Output"]);
    }

    [Fact]
    public async Task BrowseOutputDirectoryCommand_CallsSavePreferencesAsync()
    {
        _dialogs.Folder = @"C:\Selected\Output";

        await PickFolderHandler(Create()).Handle(new PickOutputFolderRequest(), default);

        _settings.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task BrowseOutputDirectoryCommand_WhenCancelled_DoesNotChangeOutput()
    {
        var session = Create();
        session.SetOutputFolder(@"C:\Original");
        _dialogs.Folder = null;

        var snapshot = await PickFolderHandler(session).Handle(new PickOutputFolderRequest(), default);

        snapshot.OutputFolder.ShouldBe(@"C:\Original");
    }

    [Fact]
    public async Task PickSymbolMap_SetsTheSymbolMapPath()
    {
        var session = Create();
        session.SetWriteSymbolMap(true);
        _dialogs.SavePath = @"C:\Maps\my.json";

        var snapshot = await PickMapHandler(session).Handle(new PickSymbolMapRequest(), default);

        _dialogs.SavePicks.ShouldBe(1);
        snapshot.SymbolMapPath.ShouldBe(@"C:\Maps\my.json");
    }

    [Fact]
    public async Task PickSymbolMap_WhenCancelled_LeavesThePathAlone()
    {
        var session = Create();
        session.SetWriteSymbolMap(true);
        session.SetSymbolMapPath("maps/m.json");
        _dialogs.SavePath = null;

        var snapshot = await PickMapHandler(session).Handle(new PickSymbolMapRequest(), default);

        _dialogs.SavePicks.ShouldBe(1);
        snapshot.SymbolMapPath.ShouldBe("maps/m.json");
    }

    #endregion

    #region ResetFileStatus Tests

    [Fact]
    public void ResetFileStatus_SetsAllFilesToPending()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("test1.dll"), CreateTestFile("test2.dll")]);
        var rows = session.GetSnapshot().Files;
        session.SetFileResult(rows[0].Id, FileStatus.Success);
        session.SetFileResult(rows[1].Id, FileStatus.Error, error: "boom");

        session.ResetStatus();

        session.GetSnapshot().Files.ShouldAllBe(f => f.Status == FileStatus.Pending);
    }

    // The WPF row carried a progress number a run advanced; the page gets progress from the run, not the list, so the
    // port checks what a run leaves on a row instead: the result it recorded.
    [Fact]
    public void SetFileResult_RecordsStatusOutputPathAndError()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("test.dll")]);
        var id = Only(session).Id;
        var output = Path.Combine(_tempDirectory, "test.obfuscated.dll");

        session.SetFileResult(id, FileStatus.Success, outputPath: output);

        var row = Only(session);
        row.Status.ShouldBe(FileStatus.Success);
        row.OutputPath.ShouldBe(output);
        row.Included.ShouldBeTrue();
    }

    [Fact]
    public void ResetFileStatus_ClearsErrorMessages()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("test.dll")]);
        session.SetFileResult(Only(session).Id, FileStatus.Error, error: "Some error");
        Only(session).Error.ShouldBe("Some error");

        session.ResetStatus();

        Only(session).Error.ShouldBeNull();
    }

    #endregion

    // ---- Defect fixes (written first, seen failing, then fixed) ----

    [Fact]
    public void OutputName_HonoursIncludeCheckbox()
    {
        // UI-053: the output name, the symbol-map location and the mode hint all follow the included files, by one rule.
        var session = Create();
        var first = CreateTestFile("first.dll", "dirA");
        var second = CreateTestFile("second.dll", "dirB");
        session.AddPaths([first, second]);
        session.SetWriteSymbolMap(true);

        var merged = session.PlanOutput(merge: true);
        merged.Mode.ShouldBe(OutputMode.Merge);
        merged.MergePath.ShouldBe(Path.Combine(_tempDirectory, "dirA", "first.obfuscated.dll"));

        // Exclude the first: only the second is left, and every name follows it.
        session.SetIncluded(session.GetSnapshot().Files.Single(f => f.Name == "first.dll").Id, false);

        var plan = session.PlanOutput(merge: true);
        plan.Mode.ShouldBe(OutputMode.Each);
        plan.MergePath.ShouldBeNull();
        plan.Hint.ShouldContain("second.obfuscated.dll");
        plan.Hint.ShouldNotContain("first");
        session.ResolveSymbolMapPath().ShouldBe(Path.Combine(_tempDirectory, "dirB", "symbolmap.json"));

        // The hint the page shows is the plan's: one rule for the hint and the run.
        var snapshot = session.GetSnapshot();
        snapshot.OutputHint.ShouldBe(session.PlanOutput(merge: false).Hint);
        snapshot.MergeOutputHint.ShouldBe(plan.Hint);
        snapshot.ResolvedSymbolMapPath.ShouldBe(session.ResolveSymbolMapPath());
    }

    [Fact]
    public void ReincludedRow_KeepsOriginalSkipReason()
    {
        // UI-054: excluding a row again must not replace the reason the analysis gave it with a generic one.
        var session = Create(analyzer: SkippedTestProjectAnalyzer("Test project"));
        session.AddPaths([CreateTestFile("App.Tests.csproj")]);
        var id = Only(session).Id;
        Only(session).SkipReason.ShouldBe("Test project");

        session.SetIncluded(id, true);
        Only(session).Status.ShouldBe(FileStatus.Pending);
        Only(session).SkipReason.ShouldBeNull();

        session.SetIncluded(id, false);

        Only(session).Status.ShouldBe(FileStatus.Skipped);
        Only(session).SkipReason.ShouldBe("Test project");
    }

    [Fact]
    public void ExcludedRow_GetsGenericReasonWhenItWasNeverSkipped()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("app.dll")]);

        session.SetIncluded(Only(session).Id, false);

        Only(session).SkipReason.ShouldBe("Excluded");
    }

    [Fact]
    public async Task Drop_ConfigWhoseSwitchesDifferFromItsPresetLevel_LoadsAsCustom_AndTheFileIsNotRewritten()
    {
        var protection = new ProtectionSession();
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Minimal);
        settings.StringEncryption.Enabled = true;
        var config = CreateTestFile("differs.json");
        await ConfigFile.SaveAsync(settings, config);
        var before = await File.ReadAllTextAsync(config);

        await Intake(Create(), protection).AddPathsAsync([config], default);

        protection.Level.ShouldBe(ObfuscationLevel.Custom);
        protection.ToObfySettings().StringEncryption.Enabled.ShouldBeTrue();
        protection.ConfigPath.ShouldBe(config);
        (await File.ReadAllTextAsync(config)).ShouldBe(before);
    }

    [Fact]
    public async Task Drop_ConfigPlusInputs_LoadsFirstConfigOnly_AndAddsInputs()
    {
        // UI-058: a .json is a drop type. With inputs dropped beside configurations, the first configuration loads, the
        // rest are named in a notice, and the inputs are added.
        var input = Create();
        var protection = new ProtectionSession();
        var firstConfig = CreateTestFile("first.json");
        await File.WriteAllTextAsync(firstConfig, AggressiveConfigJson);
        var secondConfig = CreateTestFile("second.json");
        await File.WriteAllTextAsync(secondConfig, """{ "level": "Minimal" }""");
        var dll = CreateTestFile("app.dll");

        await Intake(input, protection).AddPathsAsync([firstConfig, dll, secondConfig], default);

        protection.Level.ShouldBe(ObfuscationLevel.Aggressive);
        protection.ConfigPath.ShouldBe(firstConfig);
        Only(input).Path.ShouldBe(dll);
        var notice = input.GetSnapshot().Notices.ShouldHaveSingleItem();
        notice.Level.ShouldBe(NoticeLevel.Info);
        notice.Message.ShouldContain("first.json");
        notice.Message.ShouldContain("second.json");
    }

    [Fact]
    public async Task DropPathsRequest_AddsTheDroppedFileToTheSession()
    {
        // The handler the window's native drop reaches must change the list, not only return.
        var input = Create();
        var handler = new DropPathsRequestHandler(Intake(input));
        var dll = CreateTestFile("dropped.dll");

        await handler.Handle(new DropPathsRequest([dll, CreateTestFile("notes.txt")]), default);

        Only(input).Path.ShouldBe(dll);
    }

    // ---- Startup and threads ----

    [Fact]
    public void ApplyStartup_PublishesNothingAndRaisesNothing()
    {
        // Startup runs before the shell may be ready, and the publisher builds the web view on the thread that first
        // publishes: nothing is published from here, the page reads files.get when it loads.
        var events = new FakeEvents();
        var session = Create(events: events);
        var raised = 0;
        session.Changed += () => raised++;
        var before = session.GetSnapshot().Revision;

        session.ApplyStartup([CreateTestFile("startup.dll"), "-o", Path.Combine(_tempDirectory, "out")]);

        events.Published.ShouldBeEmpty();
        raised.ShouldBe(0);
        var snapshot = session.GetSnapshot();
        snapshot.Revision.ShouldBeGreaterThan(before);
        snapshot.Files.Count.ShouldBe(1);
    }

    [Fact]
    public async Task InitializeAsync_LoadsPreferencesWithoutPublishing()
    {
        var folder = Path.Combine(_tempDirectory, "saved");
        Directory.CreateDirectory(folder);
        _settings.OnLoad = () =>
        {
            _settings.LastOutputDirectory = folder;
            _settings.GenerateSymbolMap = true;
        };
        var events = new FakeEvents();
        var session = Create(events: events);

        await session.InitializeAsync();

        events.Published.ShouldBeEmpty();
        session.GetSnapshot().OutputFolder.ShouldBe(folder);
        session.GetSnapshot().WriteSymbolMap.ShouldBeTrue();
    }

    [Fact]
    public void Change_PublishesFilesChangedWithTheSnapshot_AfterReleasingTheLock()
    {
        // A publisher that reads the session from another thread would hang if the lock were still held.
        var events = new FakeEvents();
        var session = Create(events: events);
        events.OnPublish = () => Task.Run(session.GetSnapshot).Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();

        session.AddPaths([CreateTestFile("a.dll")]);

        var (name, data) = events.Published.ShouldHaveSingleItem();
        name.ShouldBe(AppEvents.FilesChanged);
        name.ShouldBe("files.changed");
        var snapshot = data.ShouldBeOfType<InputSnapshot>();
        snapshot.Files.Count.ShouldBe(1);
        snapshot.Revision.ShouldBe(session.GetSnapshot().Revision);
    }

    [Fact]
    public void Change_RaisesChangedOnceAndNothingWhenNothingChanged()
    {
        var session = Create();
        var raised = 0;
        session.Changed += () => raised++;
        var dll = CreateTestFile("a.dll");

        session.AddPaths([dll]);
        // A drop that adds nothing and has nothing to say changes nothing. (A duplicate now says so in a notice: see InputSessionDropTests.)
        session.AddPaths([]);

        raised.ShouldBe(1);
    }

    // ---- Rows, ids and output ----

    [Fact]
    public void FileId_IsStableForAPath_AndUniquePerFile()
    {
        var session = Create();
        var a = CreateTestFile("a.dll");
        var b = CreateTestFile("b.dll");
        session.AddPaths([a, b]);
        var ids = session.GetSnapshot().Files.Select(f => f.Id).ToList();
        ids.Distinct().Count().ShouldBe(2);

        session.Clear();
        session.AddPaths([a]);

        Only(session).Id.ShouldBe(ids[0]);
    }

    [Fact]
    public void SetIncluded_UnknownId_Throws()
    {
        var ex = Should.Throw<ObfyApiException>(() => Create().SetIncluded("nope", true));

        ex.Code.ShouldBe("files.notFound");
    }

    [Fact]
    public void SetOutputFolder_CanonicalisesTheTypedPath_AndEmptyClearsIt()
    {
        var session = Create();

        session.SetOutputFolder(@$"  {_tempDirectory}\sub\..\out\  ");
        session.GetSnapshot().OutputFolder.ShouldBe(Path.Combine(_tempDirectory, "out"));

        session.SetOutputFolder("   ");
        session.GetSnapshot().OutputFolder.ShouldBeEmpty();
        _settings.OutputAssignments.Last().ShouldBeNull();
    }

    [Fact]
    public void SetOutputFolder_RejectsAnInvalidPath_AndAFile()
    {
        var session = Create();

        Should.Throw<ObfyApiException>(() => session.SetOutputFolder("bad\0path")).Code.ShouldBe("output.invalidPath");
        Should.Throw<ObfyApiException>(() => session.SetOutputFolder(CreateTestFile("a.dll"))).Code.ShouldBe("output.isFile");
        session.GetSnapshot().OutputFolder.ShouldBeEmpty();
    }

    [Fact]
    public void SymbolMapPath_IsEditableOnlyWhileTheOptionIsOn_AndResolvesAgainstTheOutputFolder()
    {
        var session = Create();
        var output = Path.Combine(_tempDirectory, "out");
        session.SetOutputFolder(output);

        Should.Throw<ObfyApiException>(() => session.SetSymbolMapPath("maps/m.json")).Code.ShouldBe("output.symbolMapOff");
        session.GetSnapshot().ResolvedSymbolMapPath.ShouldBeNull();

        session.SetWriteSymbolMap(true);
        session.GetSnapshot().ResolvedSymbolMapPath.ShouldBe(Path.Combine(output, "symbolmap.json"));

        session.SetSymbolMapPath("maps/m.json");
        session.GetSnapshot().SymbolMapPath.ShouldBe("maps/m.json");
        session.ResolveSymbolMapPath().ShouldBe(Path.GetFullPath(Path.Combine(output, "maps", "m.json")));

        var absolute = Path.Combine(_tempDirectory, "abs.json");
        session.SetSymbolMapPath(absolute);
        session.ResolveSymbolMapPath().ShouldBe(absolute);
    }

    [Fact]
    public void PlanOutput_FollowsTheRunModeRules()
    {
        var a = CreateTestFile("a.dll", "one");
        var b = CreateTestFile("b.dll", "two");

        var single = Create();
        single.AddPaths([a]);
        single.PlanOutput(false).Mode.ShouldBe(OutputMode.Each);
        single.PlanOutput(true).Mode.ShouldBe(OutputMode.Each);

        var two = Create();
        two.AddPaths([a, b]);
        var closed = two.PlanOutput(false);
        closed.Mode.ShouldBe(OutputMode.ClosedSet);
        closed.Directory.ShouldBe(Path.Combine(_tempDirectory, "one", "obfy-out"));
        two.PlanOutput(true).Mode.ShouldBe(OutputMode.Merge);

        two.SetOutputFolder(Path.Combine(_tempDirectory, "chosen"));
        two.PlanOutput(false).Directory.ShouldBe(Path.Combine(_tempDirectory, "chosen"));
        two.PlanOutput(true).MergePath.ShouldBe(Path.Combine(_tempDirectory, "chosen", "a.obfuscated.dll"));

        Create().PlanOutput(false).Mode.ShouldBe(OutputMode.None);
    }

    [Theory]
    [InlineData("MyApp.dll")]
    [InlineData("MyApp.cs")]
    public void PlanOutput_OneFileWithAFolder_HintNamesTheFileUnderItsOwnName(string name)
    {
        // The run writes {folder}\{name}: the .obfuscated suffix is only for "next to the input".
        var folder = Path.Combine(_tempDirectory, "chosen");
        var session = Create();
        session.AddPaths([CreateTestFile(name, "in")]);
        session.SetOutputFolder(folder);

        session.PlanOutput(false).Hint.ShouldBe($"Writes {name} to {folder}.");
    }

    [Theory]
    [InlineData("MyApp.dll", "MyApp.obfuscated.dll")]
    [InlineData("MyApp.cs", "MyApp.obfuscated.cs")]
    public void PlanOutput_OneFileWithoutAFolder_HintNamesTheObfuscatedFileNextToTheInput(string name, string expected)
    {
        var session = Create();
        session.AddPaths([CreateTestFile(name, "in")]);

        session.PlanOutput(false).Hint.ShouldBe($"Writes {expected} next to the input.");
    }

    [Fact]
    public void PlanOutput_TwoSourceFilesWithoutAFolder_HintSaysTheyAreCompiledTogetherIntoObfySrcNextToTheFirst()
    {
        // ObfuscationService.ObfuscateSourcesAsync: no folder means obfy-src beside the first file, not "next to its input".
        var first = CreateTestFile("a.cs", "one");
        var second = CreateTestFile("b.cs", "two");
        var session = Create();
        session.AddPaths([first, second]);

        var plan = session.PlanOutput(false);

        plan.Mode.ShouldBe(OutputMode.Each);
        plan.Directory.ShouldBeNull();
        plan.Hint.ShouldContain(Path.Combine(_tempDirectory, "one", "obfy-src"));
        plan.Hint.ShouldContain("together");
        plan.Hint.ShouldNotContain("next to its input");
        plan.Hint.ShouldNotContain("obfuscated.");
    }

    [Fact]
    public void PlanOutput_TwoSourceFilesAndAnAssemblyWithoutAFolder_HintCoversBothWays()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("a.cs", "one"), CreateTestFile("b.cs", "two"), CreateTestFile("lib.dll", "three")]);

        var plan = session.PlanOutput(false);

        plan.Mode.ShouldBe(OutputMode.Each);
        plan.Hint.ShouldContain(Path.Combine(_tempDirectory, "one", "obfy-src"));
        plan.Hint.ShouldContain("the other file goes next to its input as name.obfuscated.ext");
    }

    [Fact]
    public void PlanOutput_SourceFilesAndAssembliesWithoutTwoSources_StayNextToTheirInputs()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("a.cs", "one"), CreateTestFile("lib.dll", "three")]);

        session.PlanOutput(false).Hint.ShouldBe("Writes each file next to its input as name.obfuscated.ext.");
    }

    [Fact]
    public void PlanOutput_SeveralFilesWithAFolder_HintSaysEachGoesThere()
    {
        var folder = Path.Combine(_tempDirectory, "chosen");
        var session = Create();
        session.AddPaths([CreateTestFile("a.cs", "one"), CreateTestFile("b.cs", "two")]);
        session.SetOutputFolder(folder);

        session.PlanOutput(false).Hint.ShouldBe($"Writes each file to {folder}.");
        session.PlanOutput(false).Directory.ShouldBe(folder);
    }

    [Fact]
    public void PlanOutput_ClosedSetAndMergeWithAFolder_HintNamesWhatTheRunWrites()
    {
        var folder = Path.Combine(_tempDirectory, "chosen");
        var session = Create();
        session.AddPaths([CreateTestFile("a.dll", "one"), CreateTestFile("b.dll", "two")]);
        session.SetOutputFolder(folder);

        var closed = session.PlanOutput(false);
        closed.Mode.ShouldBe(OutputMode.ClosedSet);
        closed.Hint.ShouldBe($"Writes the protected assemblies to {folder}.");

        var merge = session.PlanOutput(true);
        merge.Mode.ShouldBe(OutputMode.Merge);
        merge.MergePath.ShouldBe(Path.Combine(folder, "a.obfuscated.dll"));
        merge.Hint.ShouldBe($"Merges 2 assemblies into {Path.Combine(folder, "a.obfuscated.dll")}.");
    }

    [Fact]
    public void PlanOutput_ASessionRowMakesAClosedSet_AndSourceFilesCannotMerge()
    {
        var withSession = Create(analyzer: new SolutionAnalyzer());
        withSession.AddPaths([WriteAppAndTestsSolution()]);
        withSession.PlanOutput(true).Mode.ShouldBe(OutputMode.ClosedSet);

        var mixed = Create();
        mixed.AddPaths([CreateTestFile("a.dll"), CreateTestFile("b.cs")]);
        mixed.PlanOutput(true).Mode.ShouldBe(OutputMode.Each);
    }

    [Fact]
    public void IncludedInputs_LeavesOutExcludedRowsAndProjectsWithoutOutput()
    {
        var session = Create(analyzer: SkippedTestProjectAnalyzer());
        var dll = CreateTestFile("a.dll");
        var other = CreateTestFile("b.dll");
        session.AddPaths([dll, other, CreateTestFile("App.Tests.csproj")]);
        session.SetIncluded(session.GetSnapshot().Files.Single(f => f.Name == "b.dll").Id, false);
        session.SetIncluded(session.GetSnapshot().Files.Single(f => f.Name == "App.Tests.csproj").Id, true);

        session.IncludedInputs().Select(i => i.Path).ShouldBe([dll]);
    }

    // ---- Preferences ----

    [Fact]
    public async Task PreferenceSaves_AreDebounced()
    {
        var session = Create(delay: TimeSpan.FromMilliseconds(500));

        session.SetOutputFolder(@"C:\One");
        session.SetOutputFolder(@"C:\Two");
        session.SetWriteSymbolMap(true);

        Volatile.Read(ref _settings.Saves).ShouldBe(0);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref _settings.Saves) == 0 && elapsed.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(25);
        Volatile.Read(ref _settings.Saves).ShouldBe(1);
        _settings.LastOutputDirectory.ShouldBe(@"C:\Two");
        _settings.GenerateSymbolMap.ShouldBeTrue();
    }

    [Fact]
    public async Task PersistPreferences_FlushesAPendingDebounceWithoutWaitingItOut()
    {
        var session = Create(delay: TimeSpan.FromSeconds(30));
        session.SetOutputFolder(@"C:\One");
        _settings.Saves.ShouldBe(0);

        var persist = session.PersistPreferencesAsync();
        (await Task.WhenAny(persist, Task.Delay(TimeSpan.FromSeconds(5)))).ShouldBe(persist);
        await persist;

        _settings.Saves.ShouldBe(1);
        _settings.LastOutputDirectory.ShouldBe(@"C:\One");
    }

    // ---- Reveal and open ----

    [Fact]
    public void Reveal_WithoutOutput_NotifiesAndLaunchesNothing()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("a.dll")]);

        session.Reveal(Only(session).Id);

        _shell.Revealed.ShouldBeEmpty();
        var notice = session.GetSnapshot().Notices.ShouldHaveSingleItem();
        notice.Title.ShouldBe("No output yet");
        notice.Level.ShouldBe(NoticeLevel.Warning);
    }

    [Fact]
    public void Reveal_WithAnExistingOutput_SelectsItInTheFileBrowser()
    {
        var session = Create();
        session.AddPaths([CreateTestFile("a.dll")]);
        var output = CreateTestFile("a.obfuscated.dll");
        session.SetFileResult(Only(session).Id, FileStatus.Success, outputPath: output);

        session.Reveal(Only(session).Id);

        _shell.Revealed.ShouldBe([output]);
        session.GetSnapshot().Notices.ShouldBeEmpty();
    }

    [Fact]
    public void OpenOutputFolder_OpensTheChosenFolder_OrTheFolderOfTheLastOutput_OrSaysThereIsNone()
    {
        var session = Create();
        session.OpenOutputFolder();
        session.GetSnapshot().Notices.ShouldHaveSingleItem().Level.ShouldBe(NoticeLevel.Warning);

        var chosen = Path.Combine(_tempDirectory, "chosen");
        Directory.CreateDirectory(chosen);
        session.SetOutputFolder(chosen);
        session.OpenOutputFolder();
        _shell.Opened.ShouldBe([chosen]);

        session.AddPaths([CreateTestFile("a.dll")]);
        var output = CreateTestFile("a.obfuscated.dll", "results");
        session.SetFileResult(Only(session).Id, FileStatus.Success, outputPath: output);
        session.OpenOutputFolder();
        _shell.Opened.Last().ShouldBe(Path.GetDirectoryName(output));
    }

    // ---- Startup configuration, and where its publish happens ----

    private async Task<string> WriteConfigAsync(string name, string json)
    {
        var path = CreateTestFile(name);
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    [Fact]
    public async Task ApplyStartupAsync_ValidConfig_LoadsIntoProtection_AndAddsTheFilesInTheSameSet()
    {
        var input = Create();
        var protection = new ProtectionSession();
        var config = await WriteConfigAsync("startup.json", AggressiveConfigJson);
        var dll = CreateTestFile("startup.dll");
        var output = Path.Combine(_tempDirectory, "out");

        await Intake(input, protection).ApplyStartupAsync([config, dll, "-o", output]);

        protection.Level.ShouldBe(ObfuscationLevel.Aggressive);
        protection.ConfigPath.ShouldBe(config);
        var snapshot = input.GetSnapshot();
        snapshot.Files.Single().Path.ShouldBe(dll);
        snapshot.OutputFolder.ShouldBe(output);
        snapshot.Notices.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyStartupAsync_InvalidConfig_IsAnErrorNotice_AndStartupGoesOn()
    {
        var input = Create();
        var protection = new ProtectionSession();
        var config = await WriteConfigAsync("broken.json", "{ this is not json");
        var dll = CreateTestFile("startup.dll");

        await Should.NotThrowAsync(async () => await Intake(input, protection).ApplyStartupAsync([config, dll]));

        protection.Level.ShouldBe(ObfuscationLevel.Standard);
        protection.ConfigPath.ShouldBeNull();
        var snapshot = input.GetSnapshot();
        snapshot.Files.Single().Path.ShouldBe(dll);
        var notice = snapshot.Notices.ShouldHaveSingleItem();
        notice.Level.ShouldBe(NoticeLevel.Error);
        notice.Title.ShouldBe("Could not load configuration");
    }

    [Fact]
    public async Task ApplyStartupAsync_UnreadableConfig_IsAnErrorNotice_NotACrash()
    {
        var input = Create();
        var config = await WriteConfigAsync("locked.json", AggressiveConfigJson);
        using var hold = new FileStream(config, FileMode.Open, FileAccess.Read, FileShare.None);

        await Should.NotThrowAsync(async () => await Intake(input).ApplyStartupAsync([config, CreateTestFile("a.dll")]));

        input.GetSnapshot().Notices.ShouldHaveSingleItem().Level.ShouldBe(NoticeLevel.Error);
        input.GetSnapshot().Files.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ApplyStartupAsync_PublishesTheConfigLoadOnTheCallersContext()
    {
        // The startup publish is what builds the web view, and it must happen on the UI thread. The intake therefore
        // resumes on the caller's SynchronizationContext after reading the file; a ConfigureAwait(false) on that path
        // would resume on a pool thread and fail this test.
        using var pump = new PumpContext();
        var events = new FakeEvents();
        var publishedOn = new List<int>();
        events.OnPublish = () => { lock (publishedOn) publishedOn.Add(Environment.CurrentManagedThreadId); };
        var input = Create();
        var protection = new ProtectionSession(events);
        // Padding makes the read genuinely asynchronous, so there is a continuation to resume.
        var config = await WriteConfigAsync("big.json", AggressiveConfigJson + new string(' ', 4_000_000));
        var intake = new InputIntake(input, protection, IdleRun.For(input, protection), NullLogger<InputIntake>.Instance);

        await pump.RunAsync(() => intake.ApplyStartupAsync([config, CreateTestFile("a.dll")]));

        protection.Level.ShouldBe(ObfuscationLevel.Aggressive);
        pump.Posts.ShouldBeGreaterThan(0, "the read should have been asynchronous, resuming through the context");
        publishedOn.ShouldNotBeEmpty();
        publishedOn.ShouldAllBe(id => id == pump.ThreadId);
    }

    /// <summary>A single-thread message pump standing in for the UI thread's SynchronizationContext.</summary>
    private sealed class PumpContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        private readonly Thread _thread;
        private int _posts;

        public PumpContext()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                    callback(state);
            })
            { IsBackground = true };
            _thread.Start();
            ThreadId = _thread.ManagedThreadId;
        }

        public int ThreadId { get; }

        /// <summary>How many continuations were posted back to the pump.</summary>
        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);
            _queue.Add((d, state));
        }

        /// <summary>Starts <paramref name="work"/> on the pump thread and completes when it does.</summary>
        public Task RunAsync(Func<Task> work)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add((_ =>
            {
                try
                {
                    work().ContinueWith(
                        t =>
                        {
                            if (t.IsFaulted)
                                done.SetException(t.Exception!.InnerExceptions);
                            else
                                done.SetResult();
                        },
                        TaskScheduler.Default);
                }
                catch (Exception ex)
                {
                    done.SetException(ex);
                }
            }, null));
            return done.Task;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join();
        }
    }

    // ---- GetSnapshot can not be broken by a typed path ----

    [Fact]
    public void SetSymbolMapPath_RejectsAPathGetFullPathWouldReject_AndGetSnapshotStillWorks()
    {
        var session = Create();
        session.SetWriteSymbolMap(true);

        Should.Throw<ObfyApiException>(() => session.SetSymbolMapPath(new string('a', 40_000)))
            .Code.ShouldBe("output.invalidPath");

        var snapshot = Should.NotThrow(session.GetSnapshot);
        snapshot.SymbolMapPath.ShouldBeEmpty();
        snapshot.ResolvedSymbolMapPath.ShouldNotBeNull().ShouldEndWith("symbolmap.json");
    }

    [Fact]
    public void GetSnapshot_NeverThrows_WhenATypedSymbolMapPathCannotBeCombinedWithTheOutputFolder()
    {
        // Each part is a usable path alone (so the setters accept them); together they are too long for GetFullPath.
        var session = Create();
        var longFolder = Path.Combine(_tempDirectory, new string('f', 20_000));
        session.SetOutputFolder(longFolder);
        session.SetWriteSymbolMap(true);
        session.SetSymbolMapPath(new string('m', 20_000));

        var snapshot = Should.NotThrow(session.GetSnapshot);

        snapshot.SymbolMapPath.Length.ShouldBe(20_000);
        snapshot.ResolvedSymbolMapPath.ShouldBe(Path.Combine(longFolder, "symbolmap.json"));
        Should.NotThrow(session.ResolveSymbolMapPath);
    }
}
