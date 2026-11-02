namespace Assistant.Application.Ingestion;

/// <summary>CDC event schema from SPEC.md section 6.</summary>
public sealed record SourceChange(string Entity, string Id, string ChangeType, string Version, DateTimeOffset Timestamp)
{
    /// <summary>The <c>entity:id</c> key shared with <c>RetrievedPassage.SourceId</c> and the cache tags.</summary>
    public string SourceKey => $"{Entity}:{Id}";
}

/// <summary>Clean, chunk, embed and upsert a changed source document, versioned (FR-C1).</summary>
public interface IKnowledgeIndexer
{
    Task UpsertAsync(SourceChange change, CancellationToken ct);
}
