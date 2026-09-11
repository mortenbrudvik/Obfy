namespace Obfy.Core.Models;

/// <summary>
/// Marker that the anti-tamper type was injected. Presence on the pipeline context tells the
/// assembly writer to patch the integrity-hash blob after the module is written. Patching locates
/// the hash slot by a magic byte sequence in the PE image, not by a metadata token.
/// Null on the context means anti-tamper did not run.
/// </summary>
public sealed class AntiTamperMetadata
{
    /// <summary>
    /// Singleton used whenever the anti-tamper type has been injected.
    /// </summary>
    public static AntiTamperMetadata Injected { get; } = new();

    private AntiTamperMetadata()
    {
    }
}
