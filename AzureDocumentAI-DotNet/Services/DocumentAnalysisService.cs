using AzureDocumentAI_DotNet.Models;

namespace AzureDocumentAI_DotNet.Services;

public class DocumentAnalysisService
{
    public Task<DocumentIntelligenceResult> AnalyzeAsync(Stream pdf, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Document Intelligence extraction is added in Step 4.");
}
