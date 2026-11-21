namespace Obfy.App.Features.App;

/// <summary>
/// The only addresses the page can make the host open for the online guide. The page sends a topic id; the address is built
/// here from constants, so the page never names a URL. To change the guide's sections, edit <see cref="Topics"/> only.
/// </summary>
public static class GuideLinks
{
    /// <summary>The guide's address, and where an unknown or missing topic goes.</summary>
    public const string BaseUrl = "https://brudvik.com/obfy/guide";

    /// <summary>Topic id the page may send, mapped to the guide's section anchor (the part after <c>#</c>).</summary>
    public static readonly IReadOnlyDictionary<string, string> Topics = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["getting-started"] = "getting-started",
        ["files-and-output"] = "files-and-output",
        ["levels-and-settings"] = "levels-and-settings",
        ["techniques"] = "techniques",
        ["what-can-break"] = "what-can-break",
        ["results"] = "results",
        ["stack-traces"] = "stack-traces",
        ["shortcuts"] = "shortcuts",
        ["command-line"] = "command-line",
        ["ide-and-build"] = "ide-and-build",
        ["troubleshooting"] = "troubleshooting",
    };

    /// <summary>The address for <paramref name="topic"/>: its section when it is in <see cref="Topics"/>, else the guide's start.</summary>
    public static Uri Resolve(string? topic) =>
        new(topic is not null && Topics.TryGetValue(topic, out var fragment) ? $"{BaseUrl}#{fragment}" : BaseUrl);
}
