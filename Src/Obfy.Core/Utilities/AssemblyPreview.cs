using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;

namespace Obfy.Core.Utilities;

/// <summary>
/// Decompiles an assembly to C# for UI/CLI preview.
/// </summary>
public static class AssemblyPreview
{
    public static string Decompile(string assemblyPath, int maxChars = 24_000)
    {
        var decompiler = new CSharpDecompiler(assemblyPath, new DecompilerSettings
        {
            ThrowOnAssemblyResolveErrors = false
        });
        var text = decompiler.DecompileWholeModuleAsString();
        if (text.Length <= maxChars)
            return text;
        return text[..maxChars] + "\n/* truncated */";
    }
}
