namespace Obfy.App;

/// <summary>
/// An error written for people. A handler throws it to hand the page a specific <see cref="Code"/> and message;
/// any other exception reaches the page only as a generic error.
/// </summary>
public class ObfyApiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
