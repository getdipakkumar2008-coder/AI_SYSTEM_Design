namespace Assistant.Domain.Answers;

public sealed record Citation(string SourceId, string Title, string? Uri = null);
