namespace Obfy.Interop;

/// <summary>The error half of the bridge envelope: a stable <paramref name="Code"/> and a message for the user.</summary>
public record ApiError(string Code, string Message);
