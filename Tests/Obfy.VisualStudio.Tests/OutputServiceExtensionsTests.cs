using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class OutputServiceExtensionsTests
{
    [Fact]
    public async Task ActivateIfAvailableAsync_WithoutAnOutputService_CompletesWithoutThrowing()
    {
        // The post-build step runs this before it starts the CLI. A missing output service must only skip the
        // pane; awaiting a null task threw a NullReferenceException that ended the whole post-build step.
        IOutputService? missing = null;

        await Should.NotThrowAsync(() => missing.ActivateIfAvailableAsync());
    }

    [Fact]
    public async Task ActivateIfAvailableAsync_WithAnOutputService_ActivatesThePane()
    {
        var output = new RecordingOutputService();

        await ((IOutputService?)output).ActivateIfAvailableAsync();

        output.ActivateCalls.ShouldBe(1);
    }

    private sealed class RecordingOutputService : IOutputService
    {
        public int ActivateCalls { get; private set; }

        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
        public void Success(string message) { }
        public Task WriteLineAsync(string message) => Task.CompletedTask;
        public Task ClearAsync() => Task.CompletedTask;

        public Task ActivateAsync()
        {
            ActivateCalls++;
            return Task.CompletedTask;
        }
    }
}
