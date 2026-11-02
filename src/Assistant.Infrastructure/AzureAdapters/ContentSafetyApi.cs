using Azure.Core;

namespace Assistant.Infrastructure.AzureAdapters;

/// <summary>
/// Azure AI Content Safety REST client: Prompt Shields, harm category analysis and groundedness detection.
/// <paramref name="http"/> must have <c>BaseAddress</c> set to the Content Safety endpoint.
/// </summary>
public sealed class ContentSafetyApi(HttpClient http, TokenCredential credential)
{
    private const string Scope = "https://cognitiveservices.azure.com/.default";
    private const int MaxAnalyzeChars = 10_000;
    private const int MaxShieldDocuments = 5;
    private const int MaxShieldChars = 9_000;
    private static readonly string[] HarmCategories = ["Hate", "SelfHarm", "Sexual", "Violence"];

    /// <summary>True when Prompt Shields detects a user-prompt attack (jailbreak or injection).</summary>
    public async Task<bool> IsPromptAttackAsync(string userPrompt, CancellationToken ct)
    {
        var result = await AzureRest.PostJsonAsync(
            http, credential, Scope, "contentsafety/text:shieldPrompt?api-version=2024-09-01",
            new { userPrompt, documents = Array.Empty<string>() }, ct).ConfigureAwait(false);
        result.EnsureSuccess("Prompt Shields");

        return result.Body.GetProperty("userPromptAnalysis").GetProperty("attackDetected").GetBoolean();
    }

    /// <summary>
    /// Screens documents (retrieved passages) for indirect prompt injection. Returns one flag per document, true when an
    /// attack is detected. Documents are sent in batches within the service limits; a document too long to screen fully
    /// is reported as flagged (fail closed) rather than partly screened.
    /// </summary>
    public async Task<bool[]> DetectDocumentAttacksAsync(string userPrompt, IReadOnlyList<string> documents, CancellationToken ct)
    {
        var flags = new bool[documents.Count];
        var next = 0;
        while (next < documents.Count)
        {
            var batch = new List<int>();
            var chars = 0;
            while (next < documents.Count && batch.Count < MaxShieldDocuments)
            {
                var length = Math.Min(documents[next].Length, MaxShieldChars);
                if (batch.Count > 0 && chars + length > MaxShieldChars)
                {
                    break;
                }

                batch.Add(next);
                chars += length;
                next++;
            }

            var payload = batch.Select(i => documents[i].Length > MaxShieldChars ? documents[i][..MaxShieldChars] : documents[i]).ToArray();
            var result = await AzureRest.PostJsonAsync(
                http, credential, Scope, "contentsafety/text:shieldPrompt?api-version=2024-09-01",
                new { userPrompt, documents = payload }, ct).ConfigureAwait(false);
            result.EnsureSuccess("Prompt Shields (documents)");

            var analysis = result.Body.GetProperty("documentsAnalysis").EnumerateArray()
                .Select(d => d.GetProperty("attackDetected").GetBoolean()).ToArray();
            if (analysis.Length != batch.Count)
            {
                throw new InvalidOperationException("Prompt Shields returned an unexpected number of document results.");
            }

            for (var k = 0; k < batch.Count; k++)
            {
                flags[batch[k]] = analysis[k] || documents[batch[k]].Length > MaxShieldChars;
            }
        }

        return flags;
    }

    /// <summary>Highest severity (0, 2, 4 or 6) across hate, self-harm, sexual and violence for the text.</summary>
    public async Task<int> MaxHarmSeverityAsync(string text, CancellationToken ct)
    {
        var max = 0;
        foreach (var chunk in Chunk(text, MaxAnalyzeChars))
        {
            var result = await AzureRest.PostJsonAsync(
                http, credential, Scope, "contentsafety/text:analyze?api-version=2024-09-01",
                new
                {
                    text = chunk,
                    categories = HarmCategories,
                    outputType = "FourSeverityLevels",
                }, ct).ConfigureAwait(false);
            result.EnsureSuccess("Content Safety analyze");

            foreach (var category in result.Body.GetProperty("categoriesAnalysis").EnumerateArray())
            {
                max = Math.Max(max, category.GetProperty("severity").GetInt32());
            }
        }

        return max;
    }

    /// <summary>
    /// True when groundedness detection finds content not supported by the sources. Preview API
    /// (api-version 2024-09-15-preview); available in a limited set of regions.
    /// </summary>
    public async Task<bool> IsUngroundedAsync(string text, IReadOnlyList<string> groundingSources, CancellationToken ct)
    {
        var result = await AzureRest.PostJsonAsync(
            http, credential, Scope, "contentsafety/text:detectGroundedness?api-version=2024-09-15-preview",
            new
            {
                domain = "Generic",
                task = "Summarization",
                text,
                groundingSources,
                reasoning = false,
            }, ct).ConfigureAwait(false);
        result.EnsureSuccess("Groundedness detection");

        return result.Body.GetProperty("ungroundedDetected").GetBoolean();
    }

    internal static IEnumerable<string> Chunk(string text, int size)
    {
        if (text.Length == 0)
        {
            yield return text;
            yield break;
        }

        for (var i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }
}
