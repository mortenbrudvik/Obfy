namespace Obfy.UI.Services;

/// <summary>
/// Parses desktop-app startup arguments such as
/// <c>ObfyUI.exe MyApp.dll Lib.dll -o output</c>.
/// Unknown flags are ignored. The process name is not included in <paramref name="args"/>.
/// </summary>
public readonly record struct StartupCommandLine(IReadOnlyList<string> Files, string? OutputDirectory)
{
    public static StartupCommandLine Parse(IReadOnlyList<string> args)
    {
        string? output = null;
        var files = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "-o" or "--output" && i + 1 < args.Count)
            {
                output = args[++i];
                continue;
            }

            if (arg.StartsWith('-'))
                continue;

            files.Add(arg);
        }

        return new StartupCommandLine(files, output);
    }
}
