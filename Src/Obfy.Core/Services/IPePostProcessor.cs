using Obfy.Core.Pipeline;

namespace Obfy.Core.Services;

/// <summary>
/// PE-file transform that runs after <see cref="dnlib.DotNet.ModuleDef"/> write, when RVAs/file
/// offsets are known (method IL XOR, integrity hash).
/// </summary>
public interface IPePostProcessor
{
    int Order { get; }

    void Process(string pePath, PipelineContext context);
}
