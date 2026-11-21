using System.Diagnostics;
using System.IO;
using FlaUI.Core;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Launches Obfy with a private preferences file so tests do not touch %APPDATA%\Obfy.
/// </summary>
internal static class UiLaunch
{
    /// <summary>The preferences override the app reads (UiPreferences).</summary>
    public const string PreferencesVariable = "OBFY_UI_PREFERENCES";

    /// <summary>The profile override ShellViewModel reads, so a test's WebView2 never shares a profile with a real Obfy.</summary>
    public const string WebViewProfileVariable = "OBFY_WEBVIEW2_USER_DATA";

    /// <summary>
    /// WebView2 reads this itself when it starts its browser; no host code passes it on. An elevated host ignores it (and
    /// the per-user policy equivalent): only the machine policy
    /// <c>HKLM\Software\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments</c> reaches the browser, which is
    /// how ci.yml opens the debug port on GitHub's runner, whose steps run as an administrator.
    /// </summary>
    public const string BrowserArgumentsVariable = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";

    /// <summary>
    /// The browser arguments the tests need, in the variable and in the machine policy alike. Port 0 lets the browser
    /// pick a free port and write it to <see cref="DevToolsPortFile"/>, so no test has to guess one.
    /// </summary>
    public const string BrowserArguments = "--remote-debugging-port=0 --remote-allow-origins=*";

    /// <summary>
    /// Set to any value to have WebView2's browser write its own log (<c>chrome_debug.log</c> in the profile folder), for
    /// finding out why a page cannot be reached. Off by default: the log is large and slows the browser.
    /// </summary>
    public const string WebViewLogVariable = "OBFY_E2E_WEBVIEW_LOG";

    private static readonly object Gate = new();

    /// <summary>Starts the app under FlaUI. The window-level smoke test uses this; the page is not driven.</summary>
    public static Application Launch(string path, string? arguments = null)
    {
        lock (Gate)
        {
            var prefs = Path.Combine(Path.GetTempPath(), "obfy-ui-prefs-" + Guid.NewGuid().ToString("N") + ".json");
            Environment.SetEnvironmentVariable(PreferencesVariable, prefs);
            return string.IsNullOrEmpty(arguments)
                ? Application.Launch(path)
                : Application.Launch(path, arguments);
        }
    }

    /// <summary>The WebView2 profile a launch with <paramref name="workDirectory"/> uses.</summary>
    public static string ProfileFolder(string workDirectory) => Path.Combine(workDirectory, "webview2");

    /// <summary>
    /// Where the browser writes the debug port it chose (first line), once its DevTools server listens. WebView2 keeps its
    /// data in an EBWebView folder inside the profile folder.
    /// </summary>
    public static string DevToolsPortFile(string workDirectory) => Path.Combine(ProfileFolder(workDirectory), "EBWebView", "DevToolsActivePort");

    /// <summary>
    /// Starts the app with WebView2's remote-debugging port open (<see cref="DevToolsPortFile"/> says which), a private
    /// profile and private preferences, both under <paramref name="workDirectory"/>. The environment is set on the child only.
    /// </summary>
    public static Process StartWithDebugPort(string path, IReadOnlyList<string> arguments, string workDirectory)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        info.Environment[PreferencesVariable] = Path.Combine(workDirectory, "ui-preferences.json");
        info.Environment[WebViewProfileVariable] = ProfileFolder(workDirectory);
        var browserArguments = BrowserArguments;
        if (Environment.GetEnvironmentVariable(WebViewLogVariable) is { Length: > 0 })
            browserArguments += " --enable-logging --v=1";
        info.Environment[BrowserArgumentsVariable] = browserArguments;

        return Process.Start(info) ?? throw new InvalidOperationException("ObfyUI.exe did not start.");
    }
}
