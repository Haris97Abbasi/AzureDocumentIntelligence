using Azure;
using Azure.AI.DocumentIntelligence;
using AzureDocumentAI_DotNet.Models;

namespace AzureDocumentAI_DotNet.Services;

public class DocumentAnalysisService
{
    private const string ModelId = "prebuilt-layout";
    private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromMinutes(2);

    private readonly DocumentIntelligenceClient _client;

    public DocumentAnalysisService(AzureSettings settings)
    {
        _client = new DocumentIntelligenceClient(
            settings.DocumentIntelligenceEndpoint,
            new AzureKeyCredential(settings.DocumentIntelligenceKey));
    }

    public async Task<DocumentIntelligenceResult> AnalyzeAsync(Stream pdf, CancellationToken cancellationToken)
    {
        var options = new AnalyzeDocumentOptions(ModelId, await BinaryData.FromStreamAsync(pdf, cancellationToken))
        {
            Features = { DocumentAnalysisFeature.KeyValuePairs },
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AnalysisTimeout);

        AnalyzeResult result;
        try
        {
            var operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, options, timeout.Token);
            result = operation.Value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Document Intelligence did not finish within {AnalysisTimeout.TotalSeconds:F0} seconds.");
        }

        return new DocumentIntelligenceResult(
            result.Content ?? string.Empty,
            result.Tables?.Select(ToTable).ToList() ?? [],
            result.KeyValuePairs?
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key?.Content))
                .Select(pair => new ExtractedKeyValuePair(pair.Key.Content.Trim(), pair.Value?.Content?.Trim(), pair.Confidence))
                .ToList() ?? []);
    }

    private static ExtractedTable ToTable(DocumentTable table)
    {
        var rows = Enumerable.Range(0, table.RowCount)
            .Select(_ => Enumerable.Repeat(string.Empty, table.ColumnCount).ToArray())
            .ToArray();

        foreach (var cell in table.Cells)
        {
            if (cell.RowIndex < table.RowCount && cell.ColumnIndex < table.ColumnCount)
            {
                rows[cell.RowIndex][cell.ColumnIndex] = cell.Content ?? string.Empty;
            }
        }

        return new ExtractedTable(table.RowCount, table.ColumnCount, rows);
    }
}
