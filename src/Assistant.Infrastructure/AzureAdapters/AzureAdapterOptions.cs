namespace Assistant.Infrastructure.AzureAdapters;

/// <summary>
/// Settings for the Azure adapters. Bound from <c>Assistant:Azure</c>; the Bicep deployment sets these as
/// <c>Assistant__Azure__*</c> environment variables. None of these values are secrets (authentication is by managed identity).
/// </summary>
public sealed class AzureAdapterOptions
{
    public const string SectionName = "Assistant:Azure";

    public string CosmosEndpoint { get; set; } = string.Empty;

    public string CosmosDatabase { get; set; } = "assistant";

    public string SearchEndpoint { get; set; } = string.Empty;

    public string SearchIndex { get; set; } = "knowledge";

    /// <summary>When true, adds a vector query. The index must define a vectorizer (see <see cref="SearchIndexFactory"/>).</summary>
    public bool SearchUseVectors { get; set; }

    public string ContentSafetyEndpoint { get; set; } = string.Empty;

    public string LanguageEndpoint { get; set; } = string.Empty;

    /// <summary>Highest allowed harm severity. With four severity levels (0, 2, 4, 6), 4 blocks "medium" and above.</summary>
    public int BlockHarmSeverityAtOrAbove { get; set; } = 4;

    public bool RedactPii { get; set; } = true;

    public string PiiLanguage { get; set; } = "en";

    /// <summary>Groundedness detection is a preview API with limited regions, so it is opt-in.</summary>
    public bool GroundednessDetectionEnabled { get; set; }

    /// <summary>API Management gateway URL that fronts the Azure OpenAI accounts.</summary>
    public string ModelGatewayUrl { get; set; } = string.Empty;

    /// <summary>Same deployment name must exist on the primary and fallback accounts.</summary>
    public string ModelDeployment { get; set; } = "chat";

    public string ModelApiVersion { get; set; } = "2024-10-21";

    /// <summary>Token scope for the gateway: the API Management app registration, for example api://{app-id}/.default.</summary>
    public string ModelGatewayScope { get; set; } = "https://cognitiveservices.azure.com/.default";

    public int ModelMaxOutputTokens { get; set; } = 800;

    public string RedisHost { get; set; } = string.Empty;

    public int RedisPort { get; set; } = 10000;

    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(24);

    public string ServiceBusNamespace { get; set; } = string.Empty;

    public string EscalationQueue { get; set; } = "escalations";
}
