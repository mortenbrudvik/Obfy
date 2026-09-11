namespace Obfy.Core.Models;

/// <summary>
/// Metadata produced by the anti-tamper obfuscator and consumed by the assembly writer to patch the
/// integrity hash after the module is written. Passed as a typed field on the pipeline context.
/// </summary>
public class AntiTamperMetadata
{
    /// <summary>
    /// Token of the hash field, used to locate it during post-write patching.
    /// </summary>
    public uint HashFieldToken { get; set; }
}
