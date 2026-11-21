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
        catch (Exception failure)
        {
            // The app and its WebView2 are still alive here; once they are shut down nothing is left to look at. A
            // refused debugging port says nothing about why, so record what WebView2 is running and what is listening.
            var state = await DescribeWebViewStateAsync();
            await ShutDownAsync(process, playwright, browser, work);
            throw new InvalidOperationException(
                failure.Message + Environment.NewLine +
                $"WebView2 state when the connection failed (debug port {port}, ObfyUI exited: {HasExited(process)}):" +
                Environment.NewLine + state,
                failure);
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    /// <summary>
    /// The WebView2 and Obfy processes with their command lines (does the browser carry the remote-debugging
    /// argument, and which port?) and every listening TCP port with its owner (is anything listening on it?).
    /// </summary>
    private static async Task<string> DescribeWebViewStateAsync()
    {
        const string script =
            "Get-CimInstance Win32_Process -Filter \"Name='msedgewebview2.exe' or Name='ObfyUI.exe'\" | " +
            "Where-Object { $_.Name -eq 'ObfyUI.exe' -or $_.CommandLine -like '*obfy-e2e-*' } | " +
            "Select-Object ProcessId, ParentProcessId, Name, CommandLine | Format-List | Out-String -Width 600; " +
            "Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | " +
            "Select-Object LocalAddress, LocalPort, OwningProcess | Sort-Object LocalPort | Format-Table -AutoSize | Out-String -Width 200";
        try
        {
            var info = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(script);

            using var listing = Process.Start(info)!;
            var output = listing.StandardOutput.ReadToEndAsync();
            var errors = listing.StandardError.ReadToEndAsync();
            if (!listing.WaitForExit(20_000))
            {
                listing.Kill(entireProcessTree: true);
                return "(the process listing timed out)";
            }

            return await output + await errors;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "(could not list processes: " + ex.GetType().Name + ")";
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
