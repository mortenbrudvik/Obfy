using System.IO;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Obfy.UI.Services;
using Obfy.UI.ViewModels;
using Shouldly;
using Wpf.Ui;

namespace Obfy.UI.Tests.ViewModels;

public class MainViewModelTests : IDisposable
{
    private readonly Mock<IObfuscationService> _obfuscationService = new();
    private readonly Mock<IFileDialogService> _fileDialogService = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IReportService> _reportService = new();
    private readonly Mock<IContentDialogService> _contentDialogService = new();
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
        _results = new ResultsViewModel(_fileDialogService.Object, _reportService.Object);

        _viewModel = new MainViewModel(
            _obfuscationService.Object,
            _fileDialogService.Object,
            _settingsService.Object,
            _reportService.Object,
            _contentDialogService.Object,
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void ObfuscateCommand_CannotExecute_WhenNoFiles()
    {
        _viewModel.ObfuscateCommand.CanExecute(null).ShouldBeFalse();
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

    private void AddTestFile()
    {
        var path = Path.Combine(_tempDirectory, "input.dll");
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
