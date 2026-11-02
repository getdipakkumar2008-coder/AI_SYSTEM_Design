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
