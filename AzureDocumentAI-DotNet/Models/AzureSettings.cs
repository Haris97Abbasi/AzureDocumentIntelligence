namespace AzureDocumentAI_DotNet.Models;

/// <summary>
/// Azure credentials and endpoints. Read from configuration, so environment variables
/// and .NET User Secrets (in Development) both work. Never log these values.
/// </summary>
public sealed record AzureSettings(
    Uri DocumentIntelligenceEndpoint,
    string DocumentIntelligenceKey,
    Uri OpenAiEndpoint,
    string OpenAiApiKey,
    string OpenAiDeployment)
{
    private static readonly string[] RequiredNames =
    [
        "DOCUMENT_INTELLIGENCE_ENDPOINT",
        "DOCUMENT_INTELLIGENCE_KEY",
        "AZURE_OPENAI_ENDPOINT",
        "AZURE_OPENAI_API_KEY",
        "AZURE_OPENAI_DEPLOYMENT",
    ];

    public static AzureSettings Load(IConfiguration config)
    {
        var missing = RequiredNames.Where(name => string.IsNullOrWhiteSpace(config[name])).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required configuration: {string.Join(", ", missing)}. " +
                "Set them as environment variables or with 'dotnet user-secrets set <NAME> <value>'. See .env.example.");
        }

        return new AzureSettings(
            ParseEndpoint(config, "DOCUMENT_INTELLIGENCE_ENDPOINT"),
            config["DOCUMENT_INTELLIGENCE_KEY"]!,
            ParseEndpoint(config, "AZURE_OPENAI_ENDPOINT"),
            config["AZURE_OPENAI_API_KEY"]!,
            config["AZURE_OPENAI_DEPLOYMENT"]!);
    }

    private static Uri ParseEndpoint(IConfiguration config, string name) =>
        Uri.TryCreate(config[name], UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new InvalidOperationException($"{name} must be an absolute https:// URL.");

    // Keys are excluded so the record can never print secrets by accident.
    public override string ToString() =>
        $"AzureSettings {{ DocumentIntelligenceEndpoint = {DocumentIntelligenceEndpoint}, OpenAiEndpoint = {OpenAiEndpoint}, OpenAiDeployment = {OpenAiDeployment} }}";
}
