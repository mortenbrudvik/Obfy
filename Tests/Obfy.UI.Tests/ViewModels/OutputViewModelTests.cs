using Moq;
using Obfy.UI.Models;
using Obfy.UI.Services;
using Obfy.UI.ViewModels;
using Shouldly;

namespace Obfy.UI.Tests.ViewModels;

public class OutputViewModelTests
{
    private readonly Mock<IClipboardService> _clipboard = new();
    private readonly Mock<IUserNotificationService> _notifications = new();
    private readonly OutputViewModel _viewModel;

    public OutputViewModelTests()
    {
        _viewModel = new OutputViewModel(new InlineUiDispatcher(), _clipboard.Object, _notifications.Object);
    }

    [Fact]
    public void AddLog_AddsEntryWithMessageAndLevel()
    {
        _viewModel.AddLog("hello", LogLevel.Warning);

        _viewModel.Logs.Count.ShouldBe(1);
        _viewModel.Logs[0].Message.ShouldBe("hello");
        _viewModel.Logs[0].Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void Info_AddsInfoEntry()
    {
        _viewModel.Info("ready");

        _viewModel.Logs.Count.ShouldBe(1);
        _viewModel.Logs[0].Level.ShouldBe(LogLevel.Info);
        _viewModel.Logs[0].Message.ShouldBe("ready");
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        _viewModel.Info("one");
        _viewModel.Error("two");

        _viewModel.Clear();

        _viewModel.Logs.Count.ShouldBe(0);
    }

    [Fact]
    public void ClearLogsCommand_ClearsEntries()
    {
        _viewModel.Warning("keep me?");

        _viewModel.ClearLogsCommand.Execute(null);

        _viewModel.Logs.Count.ShouldBe(0);
    }

    [Fact]
    public void CopyLogsCommand_WritesFormattedTextToClipboard()
    {
        _viewModel.ShowTimestamps = false;
        _viewModel.Success("done");

        _viewModel.CopyLogsCommand.Execute(null);

        _clipboard.Verify(c => c.SetText(It.Is<string>(text => text.Contains("[Success]") && text.Contains("done"))), Times.Once);
    }

    [Fact]
    public void CopyLogsCommand_WhenClipboardThrows_ShowsErrorNotification()
    {
        _clipboard.Setup(c => c.SetText(It.IsAny<string>()))
            .Throws(new InvalidOperationException("clipboard locked"));

        _viewModel.CopyLogsCommand.Execute(null);

        _notifications.Verify(
            n => n.Show("Copy failed", "clipboard locked", NotificationSeverity.Error),
            Times.Once);
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
