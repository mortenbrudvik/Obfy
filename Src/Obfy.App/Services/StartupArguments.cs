namespace Obfy.App.Services;

/// <summary>
/// Process-start arguments such as <c>ObfyUI.exe MyApp.dll Lib.dll -o output</c>: the files, and the output folder from
/// <c>-o</c> / <c>--output</c>, given as the next argument or inline (<c>--output=dir</c>, <c>--output:dir</c>, <c>-o=dir</c>,
/// <c>-o:dir</c>). The last one wins. A flag with no value (last argument, or followed by another flag) or an empty value
/// is ignored, and is never taken for a file. Unknown flags are ignored. The process name is not part of <paramref name="args"/>.
/// </summary>
public readonly record struct StartupArguments(IReadOnlyList<string> Files, string? OutputDirectory)
{
    private static readonly string[] OutputFlags = ["-o", "--output"];

    public static StartupArguments Parse(IReadOnlyList<string> args)
    {
        string? output = null;
        var files = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (TryReadOutputFlag(arg, out var inline))
            {
                var value = inline;
                if (value is null && i + 1 < args.Count && !args[i + 1].StartsWith('-'))
                    value = args[++i];

                if (!string.IsNullOrWhiteSpace(value))
                    output = value;
                continue;
            }

            if (arg.StartsWith('-'))
                continue;

            files.Add(arg);
        }

        return new StartupArguments(files, output);
    }

    /// <summary>
    /// Whether <paramref name="arg"/> is an output flag. <paramref name="inline"/> is the value written in the same
    /// argument (after <c>=</c> or <c>:</c>), or null when the value is the next argument.
    /// </summary>
    private static bool TryReadOutputFlag(string arg, out string? inline)
    {
        inline = null;
        foreach (var flag in OutputFlags)
        {
            if (arg == flag)
                return true;

            if (arg.Length > flag.Length
                && arg.StartsWith(flag, StringComparison.Ordinal)
                && arg[flag.Length] is '=' or ':')
            {
                inline = arg[(flag.Length + 1)..];
                return true;
            }
        }

        return false;
    }
}
