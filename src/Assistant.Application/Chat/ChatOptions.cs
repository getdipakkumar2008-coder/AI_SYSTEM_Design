namespace Assistant.Application.Chat;

public sealed class ChatOptions
{
    public const string SectionName = "Assistant:Chat";

    public int MaxSteps { get; set; } = 6;

    public int MaxTokens { get; set; } = 4000;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(8);

    public int RetrievalTop { get; set; } = 5;

    public double ConfidenceThreshold { get; set; } = 0.6;

    public int HistoryTurns { get; set; } = 6;

    /// <summary>Changes whenever catalog/policy data or the model version changes; cached entries with another version are ignored.</summary>
    public string CacheVersion { get; set; } = "v1";

    public int MaxMessageLength { get; set; } = 2000;
}
