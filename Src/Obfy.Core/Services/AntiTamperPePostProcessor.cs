using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Services;

public sealed class AntiTamperPePostProcessor : IPePostProcessor
{
    public int Order => 20;

    public void Process(string pePath, PipelineContext context)
    {
        if (context.AntiTamperMetadata is not null)
            AssemblyHashComputer.PatchIntegrityHash(pePath);
    }
}
