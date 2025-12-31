namespace Obfy.Core.Models;

/// <summary>
/// Specifies the type of target being obfuscated.
/// </summary>
public enum TargetType
{
    /// <summary>
    /// .NET assembly (DLL or EXE file).
    /// </summary>
    Assembly,

    /// <summary>
    /// C# source code files.
    /// </summary>
    SourceCode
}
