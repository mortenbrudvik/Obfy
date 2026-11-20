using Obfy.Core.Utilities;

namespace Obfy.App.Services;

/// <summary>
/// Decompiles an assembly to C# text. Synchronous and CPU-heavy on purpose, like <see cref="AssemblyPreview"/>: the
/// results store calls it from <see cref="Task.Run(Action)"/>, never from the calling thread. The seam exists so tests can
/// watch which thread it runs on.
/// </summary>
public interface IAssemblyDecompiler
{
    string Decompile(string assemblyPath);
}

/// <summary>The default: <see cref="AssemblyPreview.Decompile"/>.</summary>
public sealed class AssemblyPreviewDecompiler : IAssemblyDecompiler
{
    public string Decompile(string assemblyPath) => AssemblyPreview.Decompile(assemblyPath);
}
