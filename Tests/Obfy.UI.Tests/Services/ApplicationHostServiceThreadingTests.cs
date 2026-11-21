using System.Windows.Threading;
using Moq;
using Obfy.UI.Services;
using Shouldly;

namespace Obfy.UI.Tests.Services;

/// <summary>
/// The hosted service builds a WPF window, so it must run on the UI thread. It only did because the host's lifetime happens
/// to complete StartAsync synchronously on the thread that called it; a window built on a pool thread cannot be shown.
/// </summary>
public class ApplicationHostServiceThreadingTests
{
    private sealed class UiThread : IDisposable
    {
        private readonly Thread _thread;

        public UiThread()
        {
            var ready = new ManualResetEventSlim();
            Dispatcher? dispatcher = null;
            _thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait();
            Dispatcher = dispatcher!;
        }

        public Dispatcher Dispatcher { get; }

        public int ThreadId => _thread.ManagedThreadId;

        public void Dispose()
        {
            Dispatcher.InvokeShutdown();
            _thread.Join();
        }
    }

    [Fact]
    public async Task StartAsync_FromAPoolThread_BuildsTheShellOnTheUiThread()
    {
        using var ui = new UiThread();
        int? builtOn = null;
        var service = new ApplicationHostService(
            new Mock<IServiceProvider>().Object, () => ui.Dispatcher, () => false, _ => builtOn = Environment.CurrentManagedThreadId);

        await Task.Run(() => service.StartAsync(CancellationToken.None));

        builtOn.ShouldBe(ui.ThreadId);
    }

    [Fact]
    public async Task StartAsync_AskingWhetherTheShellExists_AlsoHappensOnTheUiThread()
    {
        using var ui = new UiThread();
        int? askedOn = null;
        var built = false;
        var service = new ApplicationHostService(
            new Mock<IServiceProvider>().Object, () => ui.Dispatcher,
            () =>
            {
                askedOn = Environment.CurrentManagedThreadId;
                return true;
            },
            _ => built = true);

        await Task.Run(() => service.StartAsync(CancellationToken.None));

        askedOn.ShouldBe(ui.ThreadId);
        built.ShouldBeFalse();
    }

    [Fact]
    public async Task StartAsync_OnTheUiThread_BuildsTheShellBeforeItReturns()
    {
        using var ui = new UiThread();
        var built = false;
        var service = new ApplicationHostService(
            new Mock<IServiceProvider>().Object, () => ui.Dispatcher, () => false, _ => built = true);

        var builtBeforeReturn = await ui.Dispatcher.InvokeAsync(() =>
        {
            var task = service.StartAsync(CancellationToken.None);
            return built && task.IsCompleted;
        });

        builtBeforeReturn.ShouldBeTrue();
    }
}
