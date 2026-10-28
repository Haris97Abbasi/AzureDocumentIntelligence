using System.Text.Json;

namespace AzureDocumentAI_DotNet.Models;

public sealed record DocumentAnalysisResponse(
    string Filename,
    DocumentIntelligenceResult DocumentIntelligence,
    AiNormalizedResult AiNormalized);

public sealed record DocumentIntelligenceResult(
    string Text,
    IReadOnlyList<ExtractedTable> Tables,
    IReadOnlyList<ExtractedKeyValuePair> KeyValuePairs);

public sealed record ExtractedTable(int RowCount, int ColumnCount, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record ExtractedKeyValuePair(string Key, string? Value, float? Confidence);

public sealed record AiNormalizedResult(bool Success, JsonElement? Data, string? Error, string? RawResponse);
