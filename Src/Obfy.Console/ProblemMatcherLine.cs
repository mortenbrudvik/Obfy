namespace Obfy.Console;

/// <summary>
/// One-line diagnostics in the shape the VS Code <c>obfy</c> problem matcher captures.
/// </summary>
internal static class ProblemMatcherLine
{
    public static string Format(string file, string severity, string message, int line = 1, int column = 1)
    {
        var location = string.IsNullOrWhiteSpace(file) ? "obfy" : file.Replace('\r', ' ').Replace('\n', ' ');
        var text = string.IsNullOrWhiteSpace(message) ? "Obfuscation failed." : message.Replace('\r', ' ').Replace('\n', ' ');
        var kind = severity.Equals("warning", StringComparison.OrdinalIgnoreCase) ? "warning" : "error";
        return $"{location}({line},{column}): {kind}: {text}";
    }
}
