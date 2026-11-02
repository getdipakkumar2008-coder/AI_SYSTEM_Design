using System.Text;
using Azure.Core;

namespace Assistant.Infrastructure.AzureAdapters;

public sealed record PiiResult(string RedactedText, IReadOnlyList<string> EntityTexts);

/// <summary>
/// Azure AI Language PII detection (analyze-text, PiiEntityRecognition).
/// <paramref name="http"/> must have <c>BaseAddress</c> set to the Language endpoint.
/// </summary>
public sealed class LanguagePiiApi(HttpClient http, TokenCredential credential)
{
    private const string Scope = "https://cognitiveservices.azure.com/.default";
    private const int MaxCharsPerDocument = 5000;
    private const int MaxDocumentsPerRequest = 5;

    public async Task<PiiResult> DetectAsync(string text, string language, CancellationToken ct)
    {
        var chunks = ContentSafetyApi.Chunk(text, MaxCharsPerDocument).ToList();
        if (chunks.Count > MaxDocumentsPerRequest)
        {
            // Fail closed rather than skip PII checks on long text.
            throw new InvalidOperationException("Text too long for PII detection.");
        }

        var documents = chunks.Select((chunk, i) => new { id = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), language, text = chunk }).ToList();
        var result = await AzureRest.PostJsonAsync(
            http, credential, Scope, "language/:analyze-text?api-version=2023-04-01",
            new
            {
                kind = "PiiEntityRecognition",
                parameters = new { modelVersion = "latest" },
                analysisInput = new { documents },
            }, ct).ConfigureAwait(false);
        result.EnsureSuccess("PII detection");

        var results = result.Body.GetProperty("results");
        if (results.GetProperty("errors").GetArrayLength() > 0)
        {
            throw new InvalidOperationException("PII detection returned document errors.");
        }

        var redacted = new StringBuilder();
        var entities = new List<string>();
        foreach (var doc in results.GetProperty("documents").EnumerateArray())
        {
            redacted.Append(doc.GetProperty("redactedText").GetString());
            foreach (var entity in doc.GetProperty("entities").EnumerateArray())
            {
                entities.Add(entity.GetProperty("text").GetString() ?? string.Empty);
            }
        }

        return new PiiResult(redacted.ToString(), entities);
    }
}
