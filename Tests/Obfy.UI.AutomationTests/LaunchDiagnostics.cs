using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using FlaUI.Core;
using FlaUI.UIA3;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// What a launched ObfyUI.exe looks like from outside, for the message of a test that cannot reach its page: a debug
/// port that never opens says nothing by itself, but "the app is running and shows the WebView2 failure panel" or "a
/// dialog titled Obfy is open" says what to look at next.
/// </summary>
internal static class LaunchDiagnostics
{
    /// <summary>The automation id of the shell's native failure panel (ShellWindow.xaml).</summary>
    private const string FailurePanelId = "WebViewFailurePanel";

    public static string Describe(Process process)
    {
        var text = new StringBuilder();
        try
        {
            process.Refresh();
            if (process.HasExited)
            {
                text.Append($"ObfyUI.exe exited with code {process.ExitCode}.");
            }
            else
            {
                text.Append($"ObfyUI.exe (pid {process.Id}) is still running, responding={process.Responding}.");
                text.Append($" Its windows: {Windows(process.Id)}.");
                text.Append($" {FailurePanel(process.Id)}");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            text.Append($"ObfyUI.exe state is unavailable ({ex.Message}).");
        }

        text.Append($" msedgewebview2 processes on the machine: {CountProcesses("msedgewebview2")}.");
        if (IsElevated())
        {
            // The app inherits this token. An elevated WebView2 host ignores the browser-arguments variable, so the debug
            // port never opens unless the machine policy carries the arguments (UiLaunch.BrowserArgumentsVariable).
            text.Append($" The tests run elevated, where WebView2 ignores {UiLaunch.BrowserArgumentsVariable}: set the machine policy"
                + $@" HKLM\Software\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments (ObfyUI.exe = ""{UiLaunch.BrowserArguments}"").");
        }

        return text.ToString();
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int CountProcesses(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes)
            process.Dispose();
        return processes.Length;
    }

    /// <summary>Whether the window the app shows is the failure panel (WebView2 could not start), by UI Automation.</summary>
    private static string FailurePanel(int processId)
    {
        try
        {
            // UI Automation waits on the app; a hung one must not hang the test's failure message too.
            var found = Task.Run(() =>
            {
                using var automation = new UIA3Automation();
                using var app = Application.Attach(processId);
                var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(3));
                return window?.FindFirstDescendant(cf => cf.ByAutomationId(FailurePanelId)) is not null;
            });
            return found.Wait(TimeSpan.FromSeconds(8))
                ? found.Result ? "The native WebView2 failure panel is showing." : "The WebView2 failure panel is not showing."
                : "UI Automation did not answer in time.";
        }
        catch (Exception ex)
        {
            return $"UI Automation could not read the window ({ex.GetBaseException().Message}).";
        }
    }

    /// <summary>The process's top-level windows as <c>class "title" (visible|hidden)</c>; a message box is class #32770.</summary>
    private static string Windows(int processId)
    {
        var windows = new List<string>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var owner);
            if (owner == processId)
            {
                var title = new StringBuilder(256);
                var className = new StringBuilder(256);
                GetWindowText(handle, title, title.Capacity);
                GetClassName(handle, className, className.Capacity);
                windows.Add($"{className} \"{title}\" ({(IsWindowVisible(handle) ? "visible" : "hidden")})");
            }

            return true;
        }, 0);
        return windows.Count == 0 ? "none" : string.Join(", ", windows);
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint handle, StringBuilder text, int maxCount);
}
