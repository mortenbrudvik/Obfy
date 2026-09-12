using System.IO;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Services.Reporting;
using Obfy.UI.Models;
using Obfy.UI.Services;
using Obfy.UI.ViewModels;
using Shouldly;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Obfy.UI.Tests.ViewModels;

public class ResultsViewModelTests : IDisposable
{
    private readonly Mock<IFileDialogService> _dialogs = new();
    private readonly Mock<IReportService> _reports = new();
    private readonly Mock<IClipboardService> _clipboard = new();
    private readonly Mock<ISnackbarService> _snackbar = new();
    private readonly ResultsViewModel _viewModel;
    private readonly string _tempDirectory;

    public ResultsViewModelTests()
    {
        _viewModel = new ResultsViewModel(
            _dialogs.Object,
            _reports.Object,
            _clipboard.Object,
            _snackbar.Object);

        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ResultsVMTests_{Guid.NewGuid():N}");
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

    [Fact]
    public void LoadResults_GroupsMembersUnderType()
    {
        _viewModel.LoadResults(
            new Dictionary<string, string>
            {
                ["MyType"] = "a",
                ["MyType::Foo()"] = "b",
                ["MyType::Bar"] = "c"
            },
            new ObfuscationStatistics { TypesRenamed = 1 },
            TimeSpan.FromSeconds(1));

        _viewModel.RootNodes.Count.ShouldBe(1);
        _viewModel.RootNodes[0].OriginalName.ShouldBe("MyType");
        _viewModel.RootNodes[0].Children.Count.ShouldBe(2);
        _viewModel.HasNoSymbols.ShouldBeFalse();
        _viewModel.HasNoSearchMatches.ShouldBeFalse();
        _viewModel.HasReport.ShouldBeFalse();
    }

    [Fact]
    public void Search_KeepsMatchingHierarchy()
    {
        _viewModel.LoadResults(
            new Dictionary<string, string>
            {
                ["TypeA"] = "x",
                ["TypeA::Foo()"] = "y",
                ["TypeA::Bar()"] = "z",
                ["TypeB"] = "w"
            },
            new ObfuscationStatistics(),
            TimeSpan.Zero);

        _viewModel.SearchText = "Foo";

        _viewModel.RootNodes.Count.ShouldBe(1);
        _viewModel.RootNodes[0].OriginalName.ShouldBe("TypeA");
        _viewModel.RootNodes[0].Children.Count.ShouldBe(1);
        _viewModel.RootNodes[0].Children[0].OriginalName.ShouldBe("Foo()");
        _viewModel.HasNoSearchMatches.ShouldBeFalse();
    }

    [Fact]
    public void Search_NoMatches_SetsEmptyState()
    {
        _viewModel.LoadResults(
            new Dictionary<string, string> { ["TypeA"] = "x" },
            new ObfuscationStatistics(),
            TimeSpan.Zero);

        _viewModel.SearchText = "zzzz";

        _viewModel.RootNodes.Count.ShouldBe(0);
        _viewModel.HasNoSearchMatches.ShouldBeTrue();
        _viewModel.HasNoSymbols.ShouldBeFalse();
    }

    [Fact]
    public void Clear_ResetsReportAndTree()
    {
        _viewModel.LoadResults(
            new Dictionary<string, string> { ["TypeA"] = "x" },
            new ObfuscationStatistics(),
            TimeSpan.FromSeconds(2));
        _viewModel.SetReport(new ObfuscationReport());

        _viewModel.Clear();

        _viewModel.RootNodes.Count.ShouldBe(0);
        _viewModel.HasReport.ShouldBeFalse();
        _viewModel.HasNoSymbols.ShouldBeTrue();
        _viewModel.ExportReportCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task ExportReport_WhenDialogCancelled_DoesNotGenerate()
    {
        _viewModel.SetReport(new ObfuscationReport());
        _dialogs.Setup(d => d.ShowSaveReportDialog()).Returns((string?)null);

        await _viewModel.ExportReportCommand.ExecuteAsync(null);

        _reports.Verify(
            r => r.GenerateReportAsync(
                It.IsAny<ObfuscationReport>(),
                It.IsAny<string>(),
                It.IsAny<ReportFormat>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportReport_WhenWriteFails_ShowsErrorSnackbar()
    {
        _viewModel.SetReport(new ObfuscationReport());
        var path = Path.Combine(_tempDirectory, "report.html");
        _dialogs.Setup(d => d.ShowSaveReportDialog()).Returns(path);
        _reports
            .Setup(r => r.GenerateReportAsync(
                It.IsAny<ObfuscationReport>(),
                path,
                ReportFormat.Html,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        await _viewModel.ExportReportCommand.ExecuteAsync(null);

        VerifySnackbar(ControlAppearance.Danger, "disk full");
    }

    [Fact]
    public async Task ExportSymbolMap_WritesJsonAndShowsSuccess()
    {
        var path = Path.Combine(_tempDirectory, "map.json");
        _dialogs.Setup(d => d.ShowSaveSymbolMapDialog()).Returns(path);
        _viewModel.LoadResults(
            new Dictionary<string, string> { ["Foo"] = "a" },
            new ObfuscationStatistics(),
            TimeSpan.Zero);

        await _viewModel.ExportSymbolMapCommand.ExecuteAsync(null);

        File.Exists(path).ShouldBeTrue();
        File.ReadAllText(path).ShouldContain("Foo");
        VerifySnackbar(ControlAppearance.Success, "map.json");
    }

    [Fact]
    public void CopySymbol_UsesClipboardService()
    {
        var node = SymbolTreeNode.Create("Foo", "a", SymbolType.Type);

        _viewModel.CopySymbolCommand.Execute(node);

        _clipboard.Verify(c => c.SetText("Foo -> a"), Times.Once);
    }

    private void VerifySnackbar(ControlAppearance appearance, string messagePart)
    {
        _snackbar.Verify(
            s => s.Show(
                It.IsAny<string>(),
                It.Is<string>(m => m.Contains(messagePart, StringComparison.OrdinalIgnoreCase)),
                appearance,
                It.IsAny<IconElement?>(),
                It.IsAny<TimeSpan>()),
            Times.Once);
    }
}
