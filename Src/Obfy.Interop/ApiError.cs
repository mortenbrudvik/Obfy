namespace Obfy.Interop;

/// <summary>The error half of the bridge envelope: a stable <paramref name="Code"/> and a message for the user.</summary>
public record ApiError(string Code, string Message);

/// <summary>
/// An error written for people. A handler throws it to hand the page a specific <see cref="Code"/> and message;
/// any other exception reaches the page only as a generic error.
/// </summary>
public class ObfyApiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
