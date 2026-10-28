using System.ClientModel;
using Azure;
using AzureDocumentAI_DotNet.Models;
using AzureDocumentAI_DotNet.Services;

const long MaxUploadBytes = 10 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(AzureSettings.Load(builder.Configuration));
builder.Services.AddSingleton<DocumentAnalysisService>();
builder.Services.AddSingleton<AzureOpenAiService>();

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxUploadBytes + 1024 * 1024);

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/api/documents/analyze", async (
        IFormFile? file,
        DocumentAnalysisService documentAnalysis,
        AzureOpenAiService openAi,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (await ValidatePdfAsync(file, cancellationToken) is { } validationError)
        {
            return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        logger.LogInformation("Analyzing PDF upload ({Bytes} bytes)", file!.Length);

        try
        {
            await using var pdf = file.OpenReadStream();
            var extraction = await documentAnalysis.AnalyzeAsync(pdf, cancellationToken);
            var normalized = await openAi.NormalizeAsync(extraction, cancellationToken);

            return Results.Ok(new DocumentAnalysisResponse(Path.GetFileName(file.FileName), extraction, normalized));
        }
        catch (RequestFailedException ex)
        {
            logger.LogWarning("Azure request failed: HTTP {Status} {ErrorCode}", ex.Status, ex.ErrorCode);
            return AzureFailure(ex.Status, ex.ErrorCode);
        }
        catch (ClientResultException ex)
        {
            logger.LogWarning("Azure OpenAI request failed: HTTP {Status}", ex.Status);
            return AzureFailure(ex.Status, errorCode: null);
        }
    })
    .WithName("AnalyzeDocument")
    .WithSummary("Upload one PDF (max 10 MB) to extract text, tables and key-value pairs, then normalize them with Azure OpenAI.")
    .Accepts<IFormFile>("multipart/form-data")
    .Produces<DocumentAnalysisResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .DisableAntiforgery();

app.Run();

static async Task<string?> ValidatePdfAsync(IFormFile? file, CancellationToken cancellationToken)
{
    if (file is null)
    {
        return "No file uploaded. Send multipart/form-data with one PDF in a field named 'file'.";
    }

    if (file.Length == 0)
    {
        return "The uploaded file is empty.";
    }

    if (file.Length > MaxUploadBytes)
    {
        return $"The file is {file.Length / (1024.0 * 1024.0):F1} MB. The maximum size is 10 MB.";
    }

    var header = new byte[5];
    await using var stream = file.OpenReadStream();
    var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
    return read == header.Length && "%PDF-"u8.SequenceEqual(header)
        ? null
        : "The uploaded file is not a PDF.";
}

static IResult AzureFailure(int status, string? errorCode) =>
    Results.Problem(
        $"The Azure service call failed (HTTP {status}{(errorCode is null ? "" : $", {errorCode}")}). " +
        "Check the endpoint, key and deployment settings, then try again.",
        statusCode: StatusCodes.Status502BadGateway);
