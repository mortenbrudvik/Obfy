using System.IO;
using Moq;
using Obfy.UI.Models;
using Obfy.UI.Services;
using Obfy.UI.ViewModels;
using Shouldly;

namespace Obfy.UI.Tests.ViewModels;

public class FilesViewModelTests : IDisposable
{
    private readonly Mock<IFileDialogService> _mockFileDialogService;
    private readonly Mock<ISettingsService> _mockSettingsService;
    private readonly FilesViewModel _viewModel;
    private readonly string _tempDirectory;

    public FilesViewModelTests()
    {
        _mockFileDialogService = new Mock<IFileDialogService>();
        _mockSettingsService = new Mock<ISettingsService>();

        // Setup default return values
        _mockSettingsService.Setup(s => s.LastOutputDirectory).Returns((string?)null);
        _mockSettingsService.Setup(s => s.GenerateSymbolMap).Returns(false);

        _viewModel = new FilesViewModel(_mockFileDialogService.Object, _mockSettingsService.Object);

        // Create temp directory for test files
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"FilesVMTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
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

    private string CreateTestFile(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    #region Initial State Tests

    [Fact]
    public void Constructor_InitializesEmptyFileList()
    {
        // Assert
        _viewModel.Files.Count.ShouldBe(0);
    }

    [Fact]
    public void Constructor_InitializesHasFilesToFalse()
    {
        // Assert
        _viewModel.HasFiles.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_InitializesHasNoFilesToTrue()
    {
        // Assert
        _viewModel.HasNoFiles.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_LoadsLastOutputDirectoryFromSettings()
    {
        // Arrange
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.LastOutputDirectory).Returns(@"C:\Output");
        mockSettings.Setup(s => s.GenerateSymbolMap).Returns(false);

        // Act
        var viewModel = new FilesViewModel(_mockFileDialogService.Object, mockSettings.Object);

        // Assert
        viewModel.OutputDirectory.ShouldBe(@"C:\Output");
    }

    [Fact]
    public void Constructor_LoadsGenerateSymbolMapFromSettings()
    {
        // Arrange
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.GenerateSymbolMap).Returns(true);

        // Act
        var viewModel = new FilesViewModel(_mockFileDialogService.Object, mockSettings.Object);

        // Assert
        viewModel.GenerateSymbolMap.ShouldBeTrue();
    }

    [Fact]
    public void ApplyPreferences_CopiesLoadedValuesFromSettings()
    {
        // Arrange — ctor snapshots empty values; ApplyPreferences is the post-load fix
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.LastOutputDirectory).Returns((string?)null);
        mockSettings.Setup(s => s.GenerateSymbolMap).Returns(false);

        var viewModel = new FilesViewModel(_mockFileDialogService.Object, mockSettings.Object);
        viewModel.OutputDirectory.ShouldBe(string.Empty);
        viewModel.GenerateSymbolMap.ShouldBeFalse();

        mockSettings.Setup(s => s.LastOutputDirectory).Returns(@"C:\Loaded\Output");
        mockSettings.Setup(s => s.GenerateSymbolMap).Returns(true);

        // Act
        viewModel.ApplyPreferences();

        // Assert
        viewModel.OutputDirectory.ShouldBe(@"C:\Loaded\Output");
        viewModel.GenerateSymbolMap.ShouldBeTrue();
    }

    #endregion

    #region HandleFileDrop Tests

    [Fact]
    public void HandleFileDrop_WithValidDll_AddsFile()
    {
        // Arrange
        var dllPath = CreateTestFile("test.dll");

        // Act
        _viewModel.HandleFileDrop(new[] { dllPath });

        // Assert
        _viewModel.Files.Count.ShouldBe(1);
        _viewModel.Files[0].FilePath.ShouldBe(dllPath);
    }

    [Fact]
    public void ApplyStartup_AddsFilesAndOutputDirectory()
    {
        var dllPath = CreateTestFile("startup.dll");
        var output = Path.Combine(_tempDirectory, "out");

        _viewModel.ApplyStartup(new StartupCommandLine([dllPath], output));

        _viewModel.Files.Count.ShouldBe(1);
        _viewModel.Files[0].FilePath.ShouldBe(dllPath);
        _viewModel.OutputDirectory.ShouldBe(output);
    }

    [Fact]
    public void HandleFileDrop_WithValidExe_AddsFile()
    {
        // Arrange
        var exePath = CreateTestFile("test.exe");

        // Act
        _viewModel.HandleFileDrop(new[] { exePath });

        // Assert
        _viewModel.Files.Count.ShouldBe(1);
    }

    [Fact]
    public void HandleFileDrop_WithCsFile_AddsFile()
    {
        // Arrange
        var csPath = CreateTestFile("Program.cs");

        // Act
        _viewModel.HandleFileDrop(new[] { csPath });

        // Assert
        _viewModel.Files.Count.ShouldBe(1);
    }

    [Fact]
    public void HandleFileDrop_WithInvalidExtension_IgnoresFile()
    {
        // Arrange
        var txtPath = CreateTestFile("readme.txt");

        // Act
        _viewModel.HandleFileDrop(new[] { txtPath });

        // Assert
        _viewModel.Files.Count.ShouldBe(0);
    }

    [Fact]
    public void HandleFileDrop_WithNonExistentFile_IgnoresFile()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "nonexistent.dll");

        // Act
        _viewModel.HandleFileDrop(new[] { nonExistentPath });

        // Assert
        _viewModel.Files.Count.ShouldBe(0);
    }

    [Fact]
    public void HandleFileDrop_WithDuplicateFile_IgnoresDuplicate()
    {
        // Arrange
        var dllPath = CreateTestFile("test.dll");
        _viewModel.HandleFileDrop(new[] { dllPath });

        // Act
        _viewModel.HandleFileDrop(new[] { dllPath }); // Add same file again

        // Assert
        _viewModel.Files.Count.ShouldBe(1);
    }

    [Fact]
    public void HandleFileDrop_WithMultipleFiles_AddsAll()
    {
        // Arrange
        var dll1 = CreateTestFile("test1.dll");
        var dll2 = CreateTestFile("test2.dll");
        var exe = CreateTestFile("app.exe");

        // Act
        _viewModel.HandleFileDrop(new[] { dll1, dll2, exe });

        // Assert
        _viewModel.Files.Count.ShouldBe(3);
    }

    [Fact]
    public void HandleFileDrop_UpdatesHasFiles()
    {
        // Arrange
        var dllPath = CreateTestFile("test.dll");
        _viewModel.HasFiles.ShouldBeFalse();

        // Act
        _viewModel.HandleFileDrop(new[] { dllPath });

        // Assert
        _viewModel.HasFiles.ShouldBeTrue();
        _viewModel.HasNoFiles.ShouldBeFalse();
    }

    #endregion

    [Fact]
    public void CanAcceptDrop_TrueWhenAnySupportedFile()
    {
        var dll = CreateTestFile("ok.dll");
        FilesViewModel.CanAcceptDrop(new[] { dll, CreateTestFile("no.txt") }).ShouldBeTrue();
        FilesViewModel.CanAcceptDrop(new[] { CreateTestFile("no.txt") }).ShouldBeFalse();
        FilesViewModel.CanAcceptDrop(null).ShouldBeFalse();
        FilesViewModel.IsSupportedInputPath(":::not-a-path").ShouldBeFalse();
    }

    [Fact]
    public void HandleFileDrop_WithInvalidPath_DoesNotThrow()
    {
        Should.NotThrow(() => _viewModel.HandleFileDrop(new[] { "\0invalid", "", @"C:\nope\missing.dll" }));
        _viewModel.Files.Count.ShouldBe(0);
    }

    [Fact]
    public void AddSourceFilesCommand_AddsCsFiles()
    {
        var cs = CreateTestFile("Program.cs");
        _mockFileDialogService.Setup(s => s.ShowOpenSourceDialog()).Returns(new[] { cs });

        _viewModel.AddSourceFilesCommand.Execute(null);

        _viewModel.Files.Count.ShouldBe(1);
        _viewModel.Files[0].IsSourceFile.ShouldBeTrue();
    }

    #region AddFiles Command Tests

    [Fact]
    public void AddFilesCommand_CallsFileDialogService()
    {
        // Arrange
        _mockFileDialogService.Setup(s => s.ShowOpenAssemblyDialog())
            .Returns(Array.Empty<string>());

        // Act
        _viewModel.AddFilesCommand.Execute(null);

        // Assert
        _mockFileDialogService.Verify(s => s.ShowOpenAssemblyDialog(), Times.Once);
    }

    [Fact]
    public void AddFilesCommand_AddsSelectedFiles()
    {
        // Arrange
        var dll1 = CreateTestFile("selected1.dll");
        var dll2 = CreateTestFile("selected2.dll");
        _mockFileDialogService.Setup(s => s.ShowOpenAssemblyDialog())
            .Returns(new[] { dll1, dll2 });

        // Act
        _viewModel.AddFilesCommand.Execute(null);

        // Assert
        _viewModel.Files.Count.ShouldBe(2);
    }

    #endregion

    #region RemoveFile Command Tests

    [Fact]
    public void RemoveFileCommand_RemovesFile()
    {
        // Arrange
        var dllPath = CreateTestFile("test.dll");
        _viewModel.HandleFileDrop(new[] { dllPath });
        var file = _viewModel.Files[0];

        // Act
        _viewModel.RemoveFileCommand.Execute(file);

        // Assert
        _viewModel.Files.Count.ShouldBe(0);
    }

    [Fact]
    public void RemoveFileCommand_WithNull_DoesNothing()
    {
        // Arrange
        var dllPath = CreateTestFile("test.dll");
        _viewModel.HandleFileDrop(new[] { dllPath });

        // Act
        _viewModel.RemoveFileCommand.Execute(null);

        // Assert
        _viewModel.Files.Count.ShouldBe(1);
    }

    #endregion

    #region ClearFiles Command Tests

    [Fact]
    public void ClearFilesCommand_ClearsAllFiles()
    {
        // Arrange
        var dll1 = CreateTestFile("test1.dll");
        var dll2 = CreateTestFile("test2.dll");
        _viewModel.HandleFileDrop(new[] { dll1, dll2 });

        // Act
        _viewModel.ClearFilesCommand.Execute(null);

        // Assert
        _viewModel.Files.Count.ShouldBe(0);
        _viewModel.HasNoFiles.ShouldBeTrue();
    }

    #endregion

    #region BrowseOutputDirectory Command Tests

    [Fact]
    public void BrowseOutputDirectoryCommand_CallsFileDialogService()
    {
        // Arrange
        _mockFileDialogService.Setup(s => s.ShowFolderBrowserDialog())
            .Returns((string?)null);

        // Act
        _viewModel.BrowseOutputDirectoryCommand.Execute(null);

        // Assert
        _mockFileDialogService.Verify(s => s.ShowFolderBrowserDialog(), Times.Once);
    }

    [Fact]
    public void BrowseOutputDirectoryCommand_SetsOutputDirectory()
    {
        // Arrange
        _mockFileDialogService.Setup(s => s.ShowFolderBrowserDialog())
            .Returns(@"C:\Selected\Output");

        // Act
        _viewModel.BrowseOutputDirectoryCommand.Execute(null);

        // Assert
        _viewModel.OutputDirectory.ShouldBe(@"C:\Selected\Output");
    }

    [Fact]
    public void BrowseOutputDirectoryCommand_SavesToSettings()
    {
        // Arrange
        _mockFileDialogService.Setup(s => s.ShowFolderBrowserDialog())
            .Returns(@"C:\Selected\Output");

        // Act
        _viewModel.BrowseOutputDirectoryCommand.Execute(null);

        // Assert
        _mockSettingsService.VerifySet(s => s.LastOutputDirectory = @"C:\Selected\Output", Times.Once);
    }

    [Fact]
    public async Task BrowseOutputDirectoryCommand_CallsSavePreferencesAsync()
    {
        // Arrange
        _mockFileDialogService.Setup(s => s.ShowFolderBrowserDialog())
            .Returns(@"C:\Selected\Output");
        _mockSettingsService.Setup(s => s.SavePreferencesAsync()).Returns(Task.CompletedTask);

        // Act
        await _viewModel.BrowseOutputDirectoryCommand.ExecuteAsync(null);

        // Assert
        _mockSettingsService.Verify(s => s.SavePreferencesAsync(), Times.Once);
    }

    [Fact]
    public void BrowseOutputDirectoryCommand_WhenCancelled_DoesNotChangeOutput()
    {
        // Arrange
        _viewModel.OutputDirectory = @"C:\Original";
        _mockFileDialogService.Setup(s => s.ShowFolderBrowserDialog())
            .Returns((string?)null);

        // Act
        _viewModel.BrowseOutputDirectoryCommand.Execute(null);

        // Assert
        _viewModel.OutputDirectory.ShouldBe(@"C:\Original");
    }

    #endregion

    #region ResetFileStatus Tests

    [Fact]
    public void ResetFileStatus_SetsAllFilesToPending()
    {
        // Arrange
        var dll1 = CreateTestFile("test1.dll");
        var dll2 = CreateTestFile("test2.dll");
        _viewModel.HandleFileDrop(new[] { dll1, dll2 });
        _viewModel.Files[0].Status = FileStatus.Success;
        _viewModel.Files[1].Status = FileStatus.Error;

        // Act
        _viewModel.ResetFileStatus();

        // Assert
        _viewModel.Files.ShouldAllBe(f => f.Status == FileStatus.Pending);
    }

    [Fact]
    public void ResetFileStatus_ClearsProgress()
    {
        // Arrange
        var dll = CreateTestFile("test.dll");
        _viewModel.HandleFileDrop(new[] { dll });
        _viewModel.Files[0].Progress = 50;

        // Act
        _viewModel.ResetFileStatus();

        // Assert
        _viewModel.Files[0].Progress.ShouldBe(0);
    }

    [Fact]
    public void ResetFileStatus_ClearsErrorMessages()
    {
        // Arrange
        var dll = CreateTestFile("test.dll");
        _viewModel.HandleFileDrop(new[] { dll });
        _viewModel.Files[0].ErrorMessage = "Some error";

        // Act
        _viewModel.ResetFileStatus();

        // Assert
        _viewModel.Files[0].ErrorMessage.ShouldBeNull();
    }

    #endregion
}
