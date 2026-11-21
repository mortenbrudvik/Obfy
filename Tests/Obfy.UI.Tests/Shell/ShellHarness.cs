using System.Windows.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obfy.UI.DependencyInjection;
using Obfy.UI.Services;
using Obfy.WebView;

namespace Obfy.UI.Tests.Shell;

/// <summary>
/// The app's container with the process-wide theme switched off, for tests that need the real shell view model or window.
/// WPF objects must be built and used on one STA thread, so each test runs its body through <see cref="OnSta{T}"/>.
/// </summary>
internal static class ShellHarness
{
    /// <summary>Runs <paramref name="work"/> on a new STA thread whose dispatcher is the synchronization context, as on the UI thread.</summary>
    public static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                result = work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    /// <summary>Processes the current thread's dispatcher queue until <paramref name="done"/> or the timeout.</summary>
    public static void PumpUntil(Func<bool> done, int timeoutMilliseconds = 5000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && watch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// The window's XAML names its icon by a relative pack URI, which WPF resolves against the entry assembly (here the test
    /// host), so the window cannot be built in a test as it stands. Points the resource lookup at the shell's assembly. WPF
    /// offers no public way to do it once the property has been read, so the backing field is set.
    /// </summary>
    public static void UseShellResources()
    {
        var field = typeof(System.Windows.Application).GetField(
                "_resourceAssembly", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("WPF no longer keeps its resource assembly in _resourceAssembly.");
        field.SetValue(null, typeof(Obfy.UI.Views.ShellWindow).Assembly);
    }

    public static IContainer BuildContainer(Action<ContainerBuilder>? overrides = null)
    {
        var services = new ServiceCollection();
        HybridBootstrap.AddMediatorServices(services);

        var builder = new ContainerBuilder();
        builder.Populate(services);
        HybridBootstrap.RegisterModules(builder);

        // Not the real one: it would change WPF-UI's theme and listen to Windows for the whole test process.
        builder.Register(c => new ThemeController(
                c.Resolve<ILogger<ThemeController>>(), c.Resolve<WebViewControl>(), () => false, processWide: false))
            .AsSelf()
            .SingleInstance();

        overrides?.Invoke(builder);
        return builder.Build();
    }
}
