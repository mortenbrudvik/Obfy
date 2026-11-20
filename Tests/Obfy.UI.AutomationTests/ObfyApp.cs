using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// A running ObfyUI.exe whose WebView2 page is driven by Playwright over the Chrome DevTools Protocol. The app is
/// started with <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=N</c> (WebView2 honours it by itself),
/// and Playwright attaches with <c>ConnectOverCDPAsync</c>. Every launch has its own profile and preferences under
/// <see cref="WorkDirectory"/>, so runs do not share state and never touch the user's real data.
/// </summary>
internal sealed class ObfyApp : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(60);

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

        var port = FreeTcpPort();
        var process = UiLaunch.StartWithDebugPort(exe, arguments ?? [], port, work);
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        try
        {
            playwright = await Playwright.CreateAsync();
            browser = await ConnectAsync(playwright, process, port);
            var page = await FindPageAsync(browser, process);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            return new ObfyApp(process, playwright, browser, page, work);
        }
        catch
        {
            await ShutDownAsync(process, playwright, browser, work);
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await ShutDownAsync(_process, _playwright, _browser, WorkDirectory);

    private static async Task<IBrowser> ConnectAsync(IPlaywright playwright, Process process, int port)
    {
        // The port opens only once WebView2 has started its browser process, a moment after the window shows.
        var deadline = DateTime.UtcNow + ConnectTimeout;
        while (true)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"ObfyUI.exe exited with code {process.ExitCode} before the debug port opened.");
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}");
            }
            catch (PlaywrightException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
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

        throw new TimeoutException("The Obfy page did not appear over the debug port.");
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

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
