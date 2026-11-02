namespace Assistant.Domain.Answers;

/// <summary>
/// A retrieved chunk. Its text is untrusted data, never instructions (CLAUDE.md security rules).
/// <see cref="IsPublic"/> gates semantic-cache eligibility.
/// </summary>
public sealed record RetrievedPassage(string SourceId, string Title, string Text, bool IsPublic, string? Uri = null)
{
    public Citation ToCitation() => new(SourceId, Title, Uri);
}
