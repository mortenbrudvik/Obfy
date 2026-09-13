using System.IO;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
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
        _output = new OutputViewModel(new InlineUiDispatcher(), _clipboard.Object);
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
            _results);

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
        _viewModel.Files.Files[0].Status.ShouldBe(Obfy.UI.Models.FileStatus.Success);
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

        _obfuscationService
            .SetupSequence(s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: csOut))
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: dll));

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
        _viewModel.Files.Files[0].Status.ShouldBe(Obfy.UI.Models.FileStatus.Error);
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
        _viewModel.Files.Files[0].Status.ShouldBe(Obfy.UI.Models.FileStatus.Error);
        _viewModel.Files.Files[0].ErrorMessage.ShouldBe("pipeline exploded");
        _viewModel.StatusMessage.ShouldBe("Error occurred");
        VerifySnackbar(ControlAppearance.Danger, "pipeline exploded");
    }

    [Fact]
    public async Task ObfuscateCommand_MultipleFiles_BuildsReportFromCombinedSymbols()
    {
        ObfuscationResult? captured = null;
        _obfuscationService
            .SetupSequence(s => s.ObfuscateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<ObfySettings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(
                new ObfuscationStatistics { StringsEncrypted = 2 },
                symbolMap: new Dictionary<string, string> { ["One"] = "a" }))
            .ReturnsAsync(ObfuscationResult.Successful(
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
        _viewModel.Files.Files.ShouldAllBe(f => f.Status == Obfy.UI.Models.FileStatus.Success);
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
