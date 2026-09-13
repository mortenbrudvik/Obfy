using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Services;

public sealed class MethodEncryptionPePostProcessor : IPePostProcessor
{
    public int Order => 10;

    public void Process(string pePath, PipelineContext context)
    {
        if (context.MethodEncryptionMetadata is not null)
            MethodBodyPeEncryptor.Encrypt(pePath, context.MethodEncryptionMetadata);
    }
}
