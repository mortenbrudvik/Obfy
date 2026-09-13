using Obfy.Core.Pipeline;

namespace Obfy.Core.Services;

/// <summary>
/// PE-file transform that runs after <see cref="dnlib.DotNet.ModuleDef"/> write, when RVAs/file
/// offsets are known. Method-IL XOR is Order 10, integrity hash Order 20; both no-op unless the
/// matching metadata was injected. Strong-name <c>SignInPlace</c> still runs after every processor.
/// </summary>
public interface IPePostProcessor
{
    int Order { get; }

    void Process(string pePath, PipelineContext context);
}
