using System.Globalization;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Assistant.Infrastructure.AzureAdapters;

/// <summary>
/// Azure AI Language PII detection (analyze-text, PiiEntityRecognition) with a pinned model version.
/// <paramref name="http"/> must have <c>BaseAddress</c> set to the Language endpoint.
/// </summary>
public sealed class LanguagePiiApi(HttpClient http, TokenCredential credential, IOptions<AzureAdapterOptions> options)
{
    private const string Scope = "https://cognitiveservices.azure.com/.default";
    private const int MaxCharsPerDocument = 5000;
    private const int MaxDocumentsPerRequest = 5;

    /// <summary>Overlap between consecutive windows, so an entity shorter than this cannot be split unseen across two.</summary>
    internal const int WindowOverlap = 300;

    private readonly AzureAdapterOptions _options = options.Value;

    /// <summary>
    /// Returns the text with PII masked. Redaction needs one document, so longer text fails closed instead of being
    /// split (a split could leave an entity half-masked).
    /// </summary>
    public async Task<string> RedactAsync(string text, CancellationToken ct)
    {
        if (text.Length > MaxCharsPerDocument)
        {
            throw new InvalidOperationException("Text too long to redact in one document.");
        }

        var results = await AnalyzeAsync([text], ct).ConfigureAwait(false);
        return results.GetProperty("documents")[0].GetProperty("redactedText").GetString() ?? string.Empty;
    }

    /// <summary>
    /// Returns the distinct PII entity texts found. Long text is scanned in overlapping windows so an entity that
    /// straddles a window boundary is still seen whole.
    /// </summary>
    public async Task<IReadOnlyList<string>> FindEntitiesAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var windows = Windows(text);
        if (windows.Count > MaxDocumentsPerRequest)
        {
            // Fail closed rather than skip PII checks on part of the text.
            throw new InvalidOperationException("Text too long for PII detection.");
        }

        var results = await AnalyzeAsync(windows, ct).ConfigureAwait(false);
        return results.GetProperty("documents").EnumerateArray()
            .SelectMany(doc => doc.GetProperty("entities").EnumerateArray())
            .Select(entity => entity.GetProperty("text").GetString() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    internal static List<string> Windows(string text)
    {
        if (text.Length <= MaxCharsPerDocument)
        {
            return [text];
        }

        var windows = new List<string>();
        var step = MaxCharsPerDocument - WindowOverlap;
        for (var start = 0; ; start += step)
        {
            var length = Math.Min(MaxCharsPerDocument, text.Length - start);
            windows.Add(text.Substring(start, length));
            if (start + MaxCharsPerDocument >= text.Length)
            {
                return windows;
            }
        }
    }

    private async Task<JsonElement> AnalyzeAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var documents = texts
            .Select((text, i) => new { id = (i + 1).ToString(CultureInfo.InvariantCulture), language = _options.PiiLanguage, text })
            .ToList();

        var result = await AzureRest.PostJsonAsync(
            http, credential, Scope, $"language/:analyze-text?api-version={_options.PiiApiVersion}",
            new
            {
                kind = "PiiEntityRecognition",
                parameters = new { modelVersion = _options.PiiModelVersion },
                analysisInput = new { documents },
            }, ct).ConfigureAwait(false);
        result.EnsureSuccess("PII detection");

        var results = result.Body.GetProperty("results");
        if (results.GetProperty("errors").GetArrayLength() > 0)
        {
            throw new InvalidOperationException("PII detection returned document errors.");
        }

        return results;
    }
}
