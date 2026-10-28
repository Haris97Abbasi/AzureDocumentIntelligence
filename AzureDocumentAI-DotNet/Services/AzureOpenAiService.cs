using System.ClientModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.AI.OpenAI;
using AzureDocumentAI_DotNet.Models;
using OpenAI.Chat;

namespace AzureDocumentAI_DotNet.Services;

public class AzureOpenAiService
{
    private const int MaxTextChars = 12_000;
    private const int MaxTables = 10;
    private const int MaxRowsPerTable = 50;
    private const int MaxKeyValuePairs = 100;
    private const int MaxRawResponseChars = 4_000;
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(90);

    private static readonly (string Name, Func<JsonNode?> Default)[] ExpectedFields =
    [
        ("documentType", () => null),
        ("summary", () => null),
        ("parties", () => new JsonArray()),
        ("dates", () => new JsonArray()),
        ("totals", () => new JsonArray()),
        ("keyFacts", () => new JsonArray()),
    ];

    private const string SystemPrompt =
        """
        You normalize business documents (invoices, purchase orders, receipts, contracts) into JSON.
        Use ONLY information present in the extracted document content you are given. Never guess or invent values.
        If something is unknown or absent, use null for single values and [] for lists.
        Copy amounts and dates exactly as written in the document.
        Respond with a single JSON object and nothing else, using exactly this shape:
        {
          "documentType": string | null,
          "summary": string | null,
          "parties": [ { "role": string | null, "name": string | null, "details": string | null } ],
          "dates": [ { "label": string | null, "value": string | null } ],
          "totals": [ { "label": string | null, "amount": string | null, "currency": string | null } ],
          "keyFacts": [ string ]
        }
        "summary" is one or two sentences. "keyFacts" holds up to 10 short, important facts from the document.
        """;

    private readonly ChatClient _chatClient;

    public AzureOpenAiService(AzureSettings settings)
    {
        var client = new AzureOpenAIClient(
            settings.OpenAiEndpoint,
            new ApiKeyCredential(settings.OpenAiApiKey),
            new AzureOpenAIClientOptions { NetworkTimeout = CompletionTimeout });
        _chatClient = client.GetChatClient(settings.OpenAiDeployment);
    }

    public async Task<AiNormalizedResult> NormalizeAsync(DocumentIntelligenceResult extraction, CancellationToken cancellationToken)
    {
        ChatMessage[] messages =
        [
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(BuildExtractionSummary(extraction)),
        ];
        var options = new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CompletionTimeout);

        ChatCompletion completion;
        try
        {
            completion = (await _chatClient.CompleteChatAsync(messages, options, timeout.Token)).Value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Azure OpenAI did not respond within {CompletionTimeout.TotalSeconds:F0} seconds.");
        }

        var raw = string.Concat(completion.Content.Select(part => part.Text));

        if (completion.FinishReason == ChatFinishReason.ContentFilter)
        {
            return Failure("The Azure OpenAI content filter blocked the response.", raw);
        }

        if (completion.FinishReason == ChatFinishReason.Length)
        {
            return Failure("The Azure OpenAI response was cut off before the JSON was complete.", raw);
        }

        return Parse(raw);
    }

    private static AiNormalizedResult Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Failure("Azure OpenAI returned an empty response.", raw);
        }

        JsonObject parsed;
        try
        {
            if (JsonNode.Parse(StripCodeFence(raw)) is not JsonObject root)
            {
                return Failure("Azure OpenAI returned JSON that is not an object.", raw);
            }

            parsed = root;
        }
        catch (JsonException)
        {
            return Failure("Azure OpenAI returned malformed JSON.", raw);
        }

        var normalized = new JsonObject();
        foreach (var (name, defaultValue) in ExpectedFields)
        {
            normalized[name] = parsed[name]?.DeepClone() ?? defaultValue();
        }

        using var document = JsonDocument.Parse(normalized.ToJsonString());
        return new AiNormalizedResult(true, document.RootElement.Clone(), null, null);
    }

    private static string StripCodeFence(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```"))
        {
            return text;
        }

        var firstNewLine = text.IndexOf('\n');
        var closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && closingFence > firstNewLine
            ? text[(firstNewLine + 1)..closingFence].Trim()
            : text;
    }

    private static AiNormalizedResult Failure(string error, string raw) =>
        new(false, null, error, raw.Length > MaxRawResponseChars ? raw[..MaxRawResponseChars] : raw);

    private static string BuildExtractionSummary(DocumentIntelligenceResult extraction)
    {
        var summary = new StringBuilder();

        summary.AppendLine("## Document text");
        summary.AppendLine(extraction.Text.Length > MaxTextChars
            ? extraction.Text[..MaxTextChars] + $"\n[text truncated: {extraction.Text.Length - MaxTextChars} more characters]"
            : extraction.Text);

        summary.AppendLine();
        summary.AppendLine("## Key-value pairs");
        if (extraction.KeyValuePairs.Count == 0)
        {
            summary.AppendLine("(none found)");
        }

        foreach (var pair in extraction.KeyValuePairs.Take(MaxKeyValuePairs))
        {
            summary.AppendLine($"- {pair.Key}: {pair.Value ?? "(no value)"}");
        }

        summary.AppendLine();
        summary.AppendLine("## Tables");
        if (extraction.Tables.Count == 0)
        {
            summary.AppendLine("(none found)");
        }

        foreach (var (table, index) in extraction.Tables.Take(MaxTables).Select((table, index) => (table, index)))
        {
            summary.AppendLine($"Table {index + 1} ({table.RowCount} rows x {table.ColumnCount} columns):");
            foreach (var row in table.Rows.Take(MaxRowsPerTable))
            {
                summary.AppendLine("| " + string.Join(" | ", row.Select(cell => cell.ReplaceLineEndings(" "))) + " |");
            }

            if (table.RowCount > MaxRowsPerTable)
            {
                summary.AppendLine($"[{table.RowCount - MaxRowsPerTable} more rows truncated]");
            }
        }

        return summary.ToString();
    }
}
