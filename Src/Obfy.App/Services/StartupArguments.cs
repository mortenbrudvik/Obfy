namespace Obfy.App.Services;

/// <summary>
/// Process-start arguments such as <c>ObfyUI.exe MyApp.dll Lib.dll -o output</c>: the files, and the output folder from
/// <c>-o</c> / <c>--output</c>. Unknown flags are ignored. The process name is not part of <paramref name="args"/>.
/// </summary>
public readonly record struct StartupArguments(IReadOnlyList<string> Files, string? OutputDirectory)
{
    public static StartupArguments Parse(IReadOnlyList<string> args)
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

        return new StartupArguments(files, output);
    }
}
