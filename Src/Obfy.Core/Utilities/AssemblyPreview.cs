using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;

namespace Obfy.Core.Utilities;

/// <summary>
/// Decompiles an assembly to C# for the desktop Results Preview tab (ICSharpCode.Decompiler / ILSpy engine).
/// </summary>
public static class AssemblyPreview
{
    /// <param name="maxChars">Maximum characters returned. Longer output is cut and suffixed with <c>/* truncated */</c>.</param>
    /// <returns>Whole-module C# (best-effort; missing references may omit types).</returns>
    public static string Decompile(string assemblyPath, int maxChars = 24_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        if (maxChars < 1)
            throw new ArgumentOutOfRangeException(nameof(maxChars));

        var decompiler = new CSharpDecompiler(assemblyPath, new DecompilerSettings
        {
            ThrowOnAssemblyResolveErrors = false,
            LoadInMemory = true
        });
        var text = decompiler.DecompileWholeModuleAsString();
        if (text.Length <= maxChars)
            return text;
        return text[..maxChars] + "\n/* truncated */";
    }
}
