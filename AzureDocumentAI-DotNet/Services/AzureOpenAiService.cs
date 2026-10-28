using AzureDocumentAI_DotNet.Models;

namespace AzureDocumentAI_DotNet.Services;

public class AzureOpenAiService
{
    public Task<AiNormalizedResult> NormalizeAsync(DocumentIntelligenceResult extraction, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Azure OpenAI normalization is added in Step 5.");
}
