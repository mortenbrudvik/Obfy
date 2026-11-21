using System.Diagnostics;
using System.IO;
using Microsoft.Playwright;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// A running ObfyUI.exe whose WebView2 page is driven by Playwright over the Chrome DevTools Protocol. The app is
/// started with <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=0</c> (WebView2 honours it by itself
/// unless the host is elevated, see <see cref="UiLaunch.BrowserArgumentsVariable"/>); the browser then writes the port it
/// chose to a file in the profile, and Playwright attaches with <c>ConnectOverCDPAsync</c>. Every launch has its own profile
/// and preferences under
/// <see cref="WorkDirectory"/>, so runs do not share state and never touch the user's real data.
/// </summary>
internal sealed class ObfyApp : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Set to any value to keep each launch's <see cref="WorkDirectory"/> (WebView2 profile, preferences and, with
    /// <see cref="UiLaunch.WebViewLogVariable"/>, the browser's chrome_debug.log) instead of deleting it on dispose.
    /// </summary>
    public const string KeepWorkDirectoryVariable = "OBFY_E2E_KEEP";

    private readonly Process _process;
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;

    private ObfyApp(Process process, IPlaywright playwright, IBrowser browser, IPage page, string workDirectory)
    {
        _process = process;
        _playwright = playwright;
        _browser = browser;
        Page = page;
        WorkDirectory = workDirectory;
    }

    /// <summary>The Obfy page (the Release shell serves it from https://obfy.localassets.invalid).</summary>
    public IPage Page { get; }

    /// <summary>A scratch folder owned by this launch; deleted on dispose.</summary>
    public string WorkDirectory { get; }

    /// <summary>Starts the app with <paramref name="arguments"/> (files and <c>-o</c>) and attaches to its page.</summary>
    public static async Task<ObfyApp> StartAsync(IReadOnlyList<string>? arguments = null)
    {
        var exe = UiExecutableLocator.ResolveFromTestContext(AppContext.BaseDirectory);
        var work = Path.Combine(Path.GetTempPath(), "obfy-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        var process = UiLaunch.StartWithDebugPort(exe, arguments ?? [], work);
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        try
        {
            playwright = await Playwright.CreateAsync();
            browser = await ConnectAsync(playwright, process, UiLaunch.DevToolsPortFile(work));
            var page = await FindPageAsync(browser, process);
            // Not WaitForLoadStateAsync: attached over CDP after the page has loaded, Playwright can miss the lifecycle event
            // and wait out its whole timeout (seen about one run in fifteen). The document's own state is always there to read.
            await page.WaitForFunctionAsync("() => document.readyState !== 'loading'");
            return new ObfyApp(process, playwright, browser, page, work);
        }
        catch
        {
            await ShutDownAsync(process, playwright, browser, work);
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await ShutDownAsync(_process, _playwright, _browser, WorkDirectory);

    private static async Task<IBrowser> ConnectAsync(IPlaywright playwright, Process process, string portFile)
    {
        // The browser writes its port file once its DevTools server listens, a moment after the window shows.
        var deadline = DateTime.UtcNow + ConnectTimeout;
        PlaywrightException? lastFailure = null;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"ObfyUI.exe exited with code {process.ExitCode} before the debug port opened.");

            if (ReadPort(portFile) is { } port)
            {
                try
                {
                    return await playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}");
                }
                catch (PlaywrightException ex)
                {
                    lastFailure = ex;
                }
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"WebView2's debug port did not open within {ConnectTimeout.TotalSeconds:0} s ({(File.Exists(portFile) ? "the port file exists" : "no port file at " + portFile)}). {LaunchDiagnostics.Describe(process)}",
            lastFailure);
    }

    /// <summary>The port in the first line of the browser's port file, or null while the file is missing or still being written.</summary>
    private static int? ReadPort(string portFile)
    {
        try
        {
            using var stream = new FileStream(portFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return int.TryParse(reader.ReadLine(), out var port) && port > 0 ? port : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<IPage> FindPageAsync(IBrowser browser, Process process)
    {
        // The control starts on about:blank; the app's page is the one with an http(s) address.
        var deadline = DateTime.UtcNow + ConnectTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"ObfyUI.exe exited with code {process.ExitCode} before the page loaded.");
            var page = browser.Contexts
                .SelectMany(context => context.Pages)
                .FirstOrDefault(p => p.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase));
            if (page is not null)
                return page;
            await Task.Delay(250);
        }

        throw new TimeoutException($"The Obfy page did not appear over the debug port within {ConnectTimeout.TotalSeconds:0} s. {LaunchDiagnostics.Describe(process)}");
    }

    private static async Task ShutDownAsync(Process process, IPlaywright? playwright, IBrowser? browser, string work)
    {
        try
        {
            if (browser is not null)
                await browser.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // Already gone.
        }

        playwright?.Dispose();

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and the kill.
        }
        finally
        {
            process.Dispose();
        }

        if (Environment.GetEnvironmentVariable(KeepWorkDirectoryVariable) is { Length: > 0 })
            return;

        try
        {
            Directory.Delete(work, recursive: true);
        }
        catch (IOException)
        {
            // WebView2 helpers may hold the profile a moment longer; a temp folder is not worth failing a test for.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}
