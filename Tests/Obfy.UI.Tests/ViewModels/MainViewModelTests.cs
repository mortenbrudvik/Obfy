using System.IO;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Obfy.UI.Models;
using Obfy.UI.Services;
using Obfy.UI.ViewModels;
using Shouldly;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Obfy.UI.Tests.ViewModels;

public class MainViewModelTests : IDisposable
{
    private readonly Mock<IObfuscationService> _obfuscationService = new();
    private readonly Mock<IFileDialogService> _fileDialogService = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IReportService> _reportService = new();
    private readonly Mock<IContentDialogService> _contentDialogService = new();
    private readonly Mock<IUserNotificationService> _notifications = new();
    private readonly Mock<IClipboardService> _clipboard = new();
    private readonly SettingsViewModel _settings = new();
    private readonly FilesViewModel _files;
    private readonly OutputViewModel _output;
    private readonly ResultsViewModel _results;
    private readonly MainViewModel _viewModel;
    private readonly string _tempDirectory;

    public MainViewModelTests()
    {
        _settingsService.Setup(s => s.LastOutputDirectory).Returns((string?)null);
        _settingsService.Setup(s => s.GenerateSymbolMap).Returns(false);
        _settingsService.Setup(s => s.LoadPreferencesAsync()).Returns(Task.CompletedTask);
        _settingsService.Setup(s => s.SavePreferencesAsync()).Returns(Task.CompletedTask);
        _settingsService.Setup(s => s.SaveSettingsAsync(It.IsAny<ObfySettings>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _files = new FilesViewModel(_fileDialogService.Object, _settingsService.Object);
        _output = new OutputViewModel(new InlineUiDispatcher(), _clipboard.Object, _notifications.Object);
        _results = new ResultsViewModel(
            _fileDialogService.Object,
            _reportService.Object,
            _clipboard.Object,
            _notifications.Object);

        _viewModel = new MainViewModel(
            _obfuscationService.Object,
            _fileDialogService.Object,
            _settingsService.Object,
            _reportService.Object,
            _contentDialogService.Object,
            _notifications.Object,
            _settings,
            _files,
            _output,
            _results,
            new HelpViewModel());

        _tempDirectory = Path.Combine(Path.GetTempPath(), $"MainVMTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        _viewModel.Dispose();
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
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

    [Fact]
    public void ObfuscateCommand_CannotExecute_WhenNoFiles()
    {
        _viewModel.ObfuscateCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void ObfuscateCommand_CannotExecute_WhenOnlySkippedRows()
    {
        AddSkippedFile("App.Tests.csproj", "Test project");
        _viewModel.ObfuscateCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void ObfuscateCommand_CannotExecute_WhenWatermarkEnabledWithoutId()
    {
        AddTestFile();
        _viewModel.ObfuscateCommand.CanExecute(null).ShouldBeTrue();

        _settings.WatermarkEnabled = true;
        _settings.WatermarkId = "";
        _viewModel.ObfuscateCommand.CanExecute(null).ShouldBeFalse();

        _settings.WatermarkId = "customer-42";
        _viewModel.ObfuscateCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task ObfuscateCommand_CannotExecute_WhenObfuscating()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        _obfuscationService
            .Setup(s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                started.TrySetResult();
                return finish.Task;
            });

        AddTestFile();

        var obfuscateTask = _viewModel.ObfuscateCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _viewModel.IsObfuscating.ShouldBeTrue();
        _viewModel.ObfuscateCommand.CanExecute(null).ShouldBeFalse();

        finish.SetResult(ObfuscationResult.Successful(new ObfuscationStatistics()));
        await obfuscateTask;
    }

    [Fact]
    public async Task ObfuscateCommand_Success_UpdatesStatusAndResults()
    {
        var stats = new ObfuscationStatistics { StringsEncrypted = 4 };
        _obfuscationService
            .Setup(s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(
                stats,
                symbolMap: new Dictionary<string, string> { ["Foo"] = "a" }));

        _reportService
            .Setup(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()))
            .Returns(new ObfuscationReport());

        AddTestFile();

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _viewModel.IsObfuscating.ShouldBeFalse();
        _viewModel.StatusMessage.ShouldBe("Obfuscation complete");
        _viewModel.ShowResultsPanel.ShouldBeTrue();
        _viewModel.Files.Files[0].Status.ShouldBe(FileStatus.Success);
        _viewModel.Output.Logs.ShouldContain(l => l.Level == Obfy.UI.Models.LogLevel.Success);
        _reportService.Verify(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()), Times.Once);
        VerifySnackbar(ControlAppearance.Success, "complete");
        _viewModel.Results.HasPreviewError.ShouldBeTrue();
    }

    [Fact]
    public async Task ObfuscateCommand_Success_PreviewsLastAssemblyOutput()
    {
        var csOut = Path.Combine(_tempDirectory, "first.cs");
        File.WriteAllText(csOut, "class C {}");
        var dll = typeof(MainViewModelTests).Assembly.Location;

        SetupClosedSetSuccess(
            ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: csOut),
            ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: dll));

        _reportService
            .Setup(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()))
            .Returns(new ObfuscationReport());

        AddTestFile("a.dll");
        AddTestFile("b.dll");

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _viewModel.ShowResultsPanel.ShouldBeTrue();
        _viewModel.Results.PreviewError.ShouldBeNull();
        _viewModel.Results.PreviewText.ShouldNotBeEmpty();
        _viewModel.Results.HasPreview.ShouldBeTrue();
    }

    [Fact]
    public async Task ObfuscateCommand_Failure_SetsError()
    {
        _obfuscationService
            .Setup(s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Failed("encrypt failed"));

        AddTestFile();

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _viewModel.IsObfuscating.ShouldBeFalse();
        _viewModel.StatusMessage.ShouldBe("Completed with errors");
        _viewModel.Files.Files[0].Status.ShouldBe(FileStatus.Error);
        _viewModel.Files.Files[0].ErrorMessage.ShouldBe("encrypt failed");
        _viewModel.Output.Logs.ShouldContain(l => l.Level == Obfy.UI.Models.LogLevel.Error && l.Message.Contains("encrypt failed"));
    }

    [Fact]
    public async Task CancelCommand_CancelsObfuscation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _obfuscationService
            .Setup(s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, string? __, ObfySettings ___, CancellationToken ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return ObfuscationResult.Successful(new ObfuscationStatistics());
            });

        AddTestFile();

        var obfuscateTask = _viewModel.ObfuscateCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _viewModel.CancelCommand.Execute(null);
        await obfuscateTask;

        _viewModel.IsObfuscating.ShouldBeFalse();
        _viewModel.StatusMessage.ShouldBe("Cancelled");
        _viewModel.Output.Logs.ShouldContain(l => l.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveConfigurationCommand_SavesSettingsToSelectedPath()
    {
        const string path = @"C:\obfy.json";
        _fileDialogService.Setup(s => s.ShowSaveConfigDialog()).Returns(path);

        await _viewModel.SaveConfigurationCommand.ExecuteAsync(null);

        _settingsService.Verify(s => s.SaveSettingsAsync(It.IsAny<ObfySettings>(), path), Times.Once);
        _viewModel.Output.Logs.ShouldContain(l => l.Message.Contains(path));
        VerifySnackbar(ControlAppearance.Success, path);
    }

    [Fact]
    public async Task LoadConfigurationCommand_LoadsSettingsFromSelectedPath()
    {
        const string path = @"C:\obfy.json";
        var settings = new ObfySettings { Level = ObfuscationLevel.Aggressive };
        _fileDialogService.Setup(s => s.ShowOpenConfigDialog()).Returns(path);
        _settingsService.Setup(s => s.LoadSettingsAsync(path)).ReturnsAsync(settings);

        await _viewModel.LoadConfigurationCommand.ExecuteAsync(null);

        _viewModel.Settings.Level.ShouldBe(ObfuscationLevel.Aggressive);
        _viewModel.Output.Logs.ShouldContain(l => l.Message.Contains(path));
    }

    [Fact]
    public async Task InitializeAsync_LoadsAndAppliesPreferences()
    {
        _settingsService.Setup(s => s.LastOutputDirectory).Returns(@"C:\Out");
        _settingsService.Setup(s => s.GenerateSymbolMap).Returns(true);

        await _viewModel.InitializeAsync();

        _settingsService.Verify(s => s.LoadPreferencesAsync(), Times.Once);
        _viewModel.Files.OutputDirectory.ShouldBe(@"C:\Out");
        _viewModel.Files.GenerateSymbolMap.ShouldBeTrue();
    }

    [Fact]
    public async Task ObfuscateCommand_Exception_MarksProcessingFileAsError()
    {
        _obfuscationService
            .Setup(s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("pipeline exploded"));

        AddTestFile();

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _viewModel.IsObfuscating.ShouldBeFalse();
        _viewModel.Files.Files[0].Status.ShouldBe(FileStatus.Error);
        _viewModel.Files.Files[0].ErrorMessage.ShouldBe("pipeline exploded");
        _viewModel.StatusMessage.ShouldBe("Error occurred");
        VerifySnackbar(ControlAppearance.Danger, "pipeline exploded");
    }

    [Fact]
    public async Task ObfuscateCommand_MultipleFiles_BuildsReportFromCombinedSymbols()
    {
        ObfuscationResult? captured = null;
        SetupClosedSetSuccess(
            ObfuscationResult.Successful(
                new ObfuscationStatistics { StringsEncrypted = 2 },
                symbolMap: new Dictionary<string, string> { ["One"] = "a" }),
            ObfuscationResult.Successful(
                new ObfuscationStatistics { StringsEncrypted = 3 },
                symbolMap: new Dictionary<string, string> { ["Two"] = "b" }));

        _reportService
            .Setup(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()))
            .Callback<ObfuscationResult, ObfySettings>((result, _) => captured = result)
            .Returns(new ObfuscationReport());

        AddTestFile("first.dll");
        AddTestFile("second.dll");

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        captured.ShouldNotBeNull();
        captured!.SymbolMap.Keys.ShouldContain("One");
        captured.SymbolMap.Keys.ShouldContain("Two");
        captured.Statistics.StringsEncrypted.ShouldBe(5);
        _reportService.Verify(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()), Times.Once);
    }

    [Fact]
    public async Task ObfuscateCommand_MergeEnabled_CallsMergeAndObfuscate()
    {
        _settings.AssemblyMergeEnabled = true;
        _obfuscationService
            .Setup(s => s.MergeAndObfuscateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(
                new ObfuscationStatistics { TypesRenamed = 1 },
                outputPath: Path.Combine(_tempDirectory, "first.obfuscated.dll")));

        _reportService
            .Setup(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()))
            .Returns(new ObfuscationReport());

        AddTestFile("first.dll");
        AddTestFile("second.dll");

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _obfuscationService.Verify(
            s => s.MergeAndObfuscateAsync(
                It.Is<IEnumerable<string>>(p => p.Count() == 2),
                It.Is<string>(path => path.Contains("obfuscated", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _obfuscationService.Verify(
            s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyClosedSetNever();
        _viewModel.Files.Files.ShouldAllBe(f => f.Status == FileStatus.Success);
    }

    [Fact]
    public async Task ObfuscateCommand_GenerateSymbolMap_WritesMap()
    {
        var mapPath = Path.Combine(_tempDirectory, "symbolmap.json");
        _files.GenerateSymbolMap = true;
        _files.SymbolMapPath = mapPath;
        _obfuscationService
            .Setup(s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(
                new ObfuscationStatistics(),
                symbolMap: new Dictionary<string, string> { ["Foo"] = "a" }));
        _obfuscationService
            .Setup(s => s.WriteSymbolMapAsync(It.IsAny<Dictionary<string, string>>(), mapPath))
            .Returns(Task.CompletedTask);
        _reportService
            .Setup(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()))
            .Returns(new ObfuscationReport());

        AddTestFile();

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _obfuscationService.Verify(
            s => s.WriteSymbolMapAsync(
                It.Is<Dictionary<string, string>>(m => m.ContainsKey("Foo")),
                mapPath),
            Times.Once);
    }

    [Fact]
    public void GetInformationalVersion_IsNotHardcodedLegacyValue()
    {
        var version = MainViewModel.GetInformationalVersion();
        version.ShouldNotBeNullOrWhiteSpace();
        version.ShouldNotBe("1.2.0");
    }

    [Fact]
    public void HelpAndAbout_CanExecute_WhenIdle()
    {
        _viewModel.ShowHelpCommand.CanExecute(null).ShouldBeTrue();
        _viewModel.ShowAboutCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void ShowHelp_CanExecute_WhileObfuscating()
    {
        _viewModel.IsObfuscating = true;
        _viewModel.HelpOpen = false;
        _viewModel.ShowHelpCommand.CanExecute(null).ShouldBeTrue();
        _viewModel.CancelCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void HelpOpen_WhileObfuscating_BlocksCancelAboutAndSecondHelp()
    {
        _viewModel.IsObfuscating = true;
        _viewModel.HelpOpen = true;

        _viewModel.CancelCommand.CanExecute(null).ShouldBeFalse();
        _viewModel.ShowHelpCommand.CanExecute(null).ShouldBeFalse();
        _viewModel.ShowAboutCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void ClearingHelpOpen_RestoresAboutAndHelp()
    {
        _viewModel.IsObfuscating = true;
        _viewModel.HelpOpen = true;
        _viewModel.HelpOpen = false;

        _viewModel.ShowHelpCommand.CanExecute(null).ShouldBeTrue();
        _viewModel.ShowAboutCommand.CanExecute(null).ShouldBeTrue();
        _viewModel.CancelCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void ShouldUseClosedSet_TwoAssemblies_IsTrue()
    {
        MainViewModel.ShouldUseClosedSet(
        [
            new AssemblyFile { FilePath = "a.dll", FileName = "a.dll" },
            new AssemblyFile { FilePath = "b.dll", FileName = "b.dll" }
        ]).ShouldBeTrue();
    }

    [Fact]
    public void ShouldUseClosedSet_OneAssembly_IsFalse()
    {
        MainViewModel.ShouldUseClosedSet(
        [
            new AssemblyFile { FilePath = "a.dll", FileName = "a.dll" }
        ]).ShouldBeFalse();
    }

    [Fact]
    public void ShouldUseClosedSet_OneAssemblyWithHints_IsTrue()
    {
        MainViewModel.ShouldUseClosedSet(
        [
            new AssemblyFile
            {
                FilePath = "a.dll",
                FileName = "a.dll",
                FromSession = true
            }
        ]).ShouldBeTrue();
    }

    [Fact]
    public void ShouldUseClosedSet_OneAssemblyWithHintsAndSkippedTest_IsTrue()
    {
        MainViewModel.ShouldUseClosedSet(
        [
            new AssemblyFile
            {
                FilePath = "App.dll",
                FileName = "App.dll",
                FromSession = true
            },
            new AssemblyFile
            {
                FilePath = "App.Tests.csproj",
                FileName = "App.Tests.csproj",
                Status = FileStatus.Skipped,
                SkipReason = "Test project"
            }
        ]).ShouldBeTrue();
    }

    [Fact]
    public async Task ObfuscateCommand_TwoAssemblies_CallsObfuscateClosedSetAsyncNotObfuscateAsync()
    {
        SetupClosedSetSuccess(
            ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: Path.Combine(_tempDirectory, "first.dll")),
            ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: Path.Combine(_tempDirectory, "second.dll")));
        _reportService
            .Setup(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()))
            .Returns(new ObfuscationReport());

        AddTestFile("first.dll");
        AddTestFile("second.dll");

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _obfuscationService.Verify(
            s => s.ObfuscateClosedSetAsync(
                It.Is<IReadOnlyList<ClosedSetInput>>(inputs =>
                    inputs.Count == 2
                    && inputs.All(i => i.Hints != null)),
                It.IsAny<string>(),
                It.IsAny<ObfySettings>(),
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _obfuscationService.Verify(
            s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _viewModel.Files.Files.ShouldAllBe(f => f.Status == FileStatus.Success);
    }

    [Fact]
    public async Task ObfuscateCommand_SkippedFile_StaysSkippedAndLogsReason()
    {
        _obfuscationService
            .Setup(s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics()));
        _reportService
            .Setup(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()))
            .Returns(new ObfuscationReport());

        AddTestFile("App.dll");
        AddSkippedFile("App.Tests.csproj", "Test project");

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _obfuscationService.Verify(
            s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyClosedSetNever();
        _viewModel.Files.Files.ShouldContain(f => f.FileName == "App.dll" && f.Status == FileStatus.Success);
        _viewModel.Files.Files.ShouldContain(f => f.FileName == "App.Tests.csproj" && f.Status == FileStatus.Skipped);
        _viewModel.Output.Logs.ShouldContain(l => l.Message == "Skipping App.Tests: Test project");
    }

    [Fact]
    public async Task ObfuscateCommand_ClosedSetFailure_MarksIncludedErrorLeavesSkipped()
    {
        _obfuscationService
            .Setup(s => s.ObfuscateClosedSetAsync(
                It.IsAny<IReadOnlyList<ClosedSetInput>>(),
                It.IsAny<string>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClosedSetResult.Failed("closed-set failed"));

        AddTestFile("first.dll");
        AddTestFile("second.dll");
        AddSkippedFile("App.Tests.csproj", "Test project");

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _viewModel.Files.Files.ShouldContain(f => f.FileName == "first.dll" && f.Status == FileStatus.Error);
        _viewModel.Files.Files.ShouldContain(f => f.FileName == "second.dll" && f.Status == FileStatus.Error);
        _viewModel.Files.Files.ShouldContain(f => f.FileName == "App.Tests.csproj" && f.Status == FileStatus.Skipped);
        _viewModel.Output.Logs.ShouldContain(l => l.Level == LogLevel.Error && l.Message.Contains("closed-set failed"));
        _viewModel.StatusMessage.ShouldBe("Completed with errors");
    }

    [Fact]
    public async Task ObfuscateCommand_ClosedSetSuccess_LoadFailureAndSourceFile_DoNotMarkSuccess()
    {
        var firstPath = Path.Combine(_tempDirectory, "first.dll");
        var secondPath = Path.Combine(_tempDirectory, "second.dll");
        _obfuscationService
            .Setup(s => s.ObfuscateClosedSetAsync(
                It.IsAny<IReadOnlyList<ClosedSetInput>>(),
                It.IsAny<string>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClosedSetResult.Succeeded(
                [
                    ObfuscationResult.Successful(
                        new ObfuscationStatistics(),
                        inputPath: firstPath,
                        outputPath: Path.Combine(_tempDirectory, "first.dll"))
                ],
                [
                    new ClosedSetLoadFailure { Path = secondPath, Message = "Bad IL" }
                ]));
        _reportService
            .Setup(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()))
            .Returns(new ObfuscationReport());

        AddTestFile("first.dll");
        AddTestFile("second.dll");
        AddTestFile("Extra.cs");

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _viewModel.Files.Files.ShouldContain(f => f.FileName == "first.dll" && f.Status == FileStatus.Success);
        var failed = _viewModel.Files.Files.Single(f => f.FileName == "second.dll");
        failed.Status.ShouldBe(FileStatus.Error);
        failed.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        var source = _viewModel.Files.Files.Single(f => f.FileName == "Extra.cs");
        source.Status.ShouldBe(FileStatus.Skipped);
        source.SkipReason.ShouldContain("Source files");
        _obfuscationService.Verify(
            s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ObfuscateCommand_PreservePublicApi_PassesForcePreservePublicTrue()
    {
        _settings.PreservePublicApi = true;
        SetupClosedSetSuccess(
            ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: Path.Combine(_tempDirectory, "first.dll")),
            ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: Path.Combine(_tempDirectory, "second.dll")));
        _reportService
            .Setup(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()))
            .Returns(new ObfuscationReport());

        AddTestFile("first.dll");
        AddTestFile("second.dll");

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _obfuscationService.Verify(
            s => s.ObfuscateClosedSetAsync(
                It.IsAny<IReadOnlyList<ClosedSetInput>>(),
                It.IsAny<string>(),
                It.Is<ObfySettings>(settings => settings.SymbolRenaming.PreservePublicApi),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ObfuscateCommand_EmptyOutputDirectory_UsesObfyOutNotInputFolder()
    {
        SetupClosedSetSuccess(
            ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: Path.Combine(_tempDirectory, "first.dll")),
            ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: Path.Combine(_tempDirectory, "second.dll")));
        _reportService
            .Setup(s => s.BuildReport(It.IsAny<ObfuscationResult>(), It.IsAny<ObfySettings>()))
            .Returns(new ObfuscationReport());

        AddTestFile("first.dll");
        AddTestFile("second.dll");
        _viewModel.Files.OutputDirectory = "";

        await _viewModel.ObfuscateCommand.ExecuteAsync(null);

        _obfuscationService.Verify(
            s => s.ObfuscateClosedSetAsync(
                It.IsAny<IReadOnlyList<ClosedSetInput>>(),
                It.Is<string>(dir => dir.EndsWith("obfy-out", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(dir, _tempDirectory, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<ObfySettings>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void VerifySnackbar(ControlAppearance appearance, string messagePart)
    {
        var severity = appearance == ControlAppearance.Danger
            ? NotificationSeverity.Error
            : appearance == ControlAppearance.Caution
                ? NotificationSeverity.Warning
                : NotificationSeverity.Success;
        _notifications.Verify(
            s => s.Show(
                It.IsAny<string>(),
                It.Is<string>(m => m.Contains(messagePart, StringComparison.OrdinalIgnoreCase)),
                severity),
            Times.AtLeastOnce);
    }

    private void AddTestFile()
        => AddTestFile("input.dll");

    private void AddTestFile(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, Array.Empty<byte>());
        _files.HandleFileDrop(new[] { path });
    }

    private void AddSkippedFile(string name, string skipReason)
    {
        _files.Files.Add(new AssemblyFile
        {
            FilePath = Path.Combine(_tempDirectory, name),
            FileName = name,
            Status = FileStatus.Skipped,
            SkipReason = skipReason
        });
    }

    private void SetupClosedSetSuccess(params ObfuscationResult[] modules)
    {
        var symbolMap = new Dictionary<string, string>();
        foreach (var module in modules)
        {
            foreach (var pair in module.SymbolMap)
                symbolMap[pair.Key] = pair.Value;
        }

        _obfuscationService
            .Setup(s => s.ObfuscateClosedSetAsync(
                It.IsAny<IReadOnlyList<ClosedSetInput>>(),
                It.IsAny<string>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClosedSetResult.Succeeded(modules, symbolMap: symbolMap));
    }

    private void VerifyClosedSetNever()
    {
        _obfuscationService.Verify(
            s => s.ObfuscateClosedSetAsync(
                It.IsAny<IReadOnlyList<ClosedSetInput>>(),
                It.IsAny<string>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }
}
