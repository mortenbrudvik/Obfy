using Obfy.Core.Models;

namespace Obfy.Tests;

/// <summary>
/// Each way of protecting an app that injects code, switched on alone (everything else off) so a problem names the
/// technique. <c>Everything</c> turns all of them on together.
/// </summary>
internal static class ProtectionCases
{
    public static readonly IReadOnlyDictionary<string, Action<ObfySettings>> All =
        new SortedDictionary<string, Action<ObfySettings>>(StringComparer.Ordinal)
        {
            ["StringsXor"] = s => { s.StringEncryption.Enabled = true; s.StringEncryption.Algorithm = EncryptionAlgorithm.Xor; },
            ["StringsAes"] = s => { s.StringEncryption.Enabled = true; s.StringEncryption.Algorithm = EncryptionAlgorithm.Aes256; },
            ["ConstantsXor"] = s => { s.ConstantEncryption.Enabled = true; s.ConstantEncryption.Algorithm = EncryptionAlgorithm.Xor; },
            ["ConstantsAes"] = s => { s.ConstantEncryption.Enabled = true; s.ConstantEncryption.Algorithm = EncryptionAlgorithm.Aes256; },
            ["ResourcesXor"] = s => { s.ResourceEncryption.Enabled = true; s.ResourceEncryption.Algorithm = EncryptionAlgorithm.Xor; },
            ["ResourcesAes"] = s => { s.ResourceEncryption.Enabled = true; s.ResourceEncryption.Algorithm = EncryptionAlgorithm.Aes256; },
            ["DependencyEmbedding"] = s =>
            {
                s.DependencyEmbedding.Enabled = true;
                s.DependencyEmbedding.IncludePatterns = new List<string> { RichSdkInput.SiblingName + ".dll" };
                s.DependencyEmbedding.ExcludePatterns = new List<string>();
            },
            ["AntiDebug"] = s => s.Protection.AntiDebug = true,
            ["AntiDump"] = s => s.Protection.AntiDump = true,
            ["AntiTamper"] = s => s.Protection.AntiTamper.Enabled = true,
            ["AntiDecompiler"] = s => s.Protection.AntiDecompiler.Enabled = true,
            ["Watermark"] = s => { s.Watermark.Enabled = true; s.Watermark.Id = "framework-targets"; },
            ["MethodEncryption"] = s => s.Protection.MethodEncryption = true,
            ["ControlFlowSwitch"] = s => { s.ControlFlow.Enabled = true; s.ControlFlow.Mode = ControlFlowMode.Switch; },
            ["ControlFlowOpaque"] = s => { s.ControlFlow.Enabled = true; s.ControlFlow.Mode = ControlFlowMode.OpaquePredicate; },
            ["ReferenceProxy"] = s => { s.Protection.ReferenceProxy = true; s.Protection.ProxyExternalCalls = true; },
            ["SymbolRenaming"] = s => s.SymbolRenaming.Enabled = true,
            ["Metadata"] = s => { s.Metadata.RemoveDebugInfo = true; s.Metadata.RemoveAttributes = true; },
            ["Virtualization"] = s => s.Virtualization.Enabled = true,
            ["VirtualizationWithRenaming"] = s => { s.Virtualization.Enabled = true; s.SymbolRenaming.Enabled = true; },
            ["Everything"] = s =>
            {
                s.StringEncryption.Enabled = true;
                s.ConstantEncryption.Enabled = true;
                s.ResourceEncryption.Enabled = true;
                s.DependencyEmbedding.Enabled = true;
                s.DependencyEmbedding.IncludePatterns = new List<string> { RichSdkInput.SiblingName + ".dll" };
                s.DependencyEmbedding.ExcludePatterns = new List<string>();
                s.Protection.AntiDebug = true;
                s.Protection.AntiDump = true;
                s.Protection.AntiTamper.Enabled = true;
                s.Protection.AntiDecompiler.Enabled = true;
                s.Protection.MethodEncryption = true;
                s.Protection.ReferenceProxy = true;
                s.Protection.ProxyExternalCalls = true;
                s.Virtualization.Enabled = true;
                s.ControlFlow.Enabled = true;
                s.SymbolRenaming.Enabled = true;
                s.Metadata.RemoveDebugInfo = true;
                s.Metadata.RemoveAttributes = true;
                s.Watermark.Enabled = true;
                s.Watermark.Id = "framework-targets";
            }
        };

    /// <summary>
    /// Whether the case puts a helper type in <c>Obfy.Runtime</c> (symbol renaming is off for the single-technique
    /// cases, so it keeps its name). A case that should but did not has proved nothing.
    /// </summary>
    public static bool InjectsRuntimeType(string name) => name is not
        ("AntiDecompiler" or "ControlFlowSwitch" or "ControlFlowOpaque" or "Metadata" or "SymbolRenaming" or "Everything"
            or "VirtualizationWithRenaming");

    /// <summary>Custom level with every default-on technique off, then the case's own switches.</summary>
    public static ObfySettings SettingsFor(string name)
    {
        var settings = new ObfySettings { Level = ObfuscationLevel.Custom };
        settings.StringEncryption.Enabled = false;
        settings.SymbolRenaming.Enabled = false;
        settings.Metadata.RemoveDebugInfo = false;
        settings.Metadata.RemoveAttributes = false;
        All[name](settings);
        return settings;
    }
}
