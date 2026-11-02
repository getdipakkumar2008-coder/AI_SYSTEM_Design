namespace Assistant.Application.Ingestion;

/// <summary>CDC event schema from SPEC.md section 6.</summary>
public sealed record SourceChange(string Entity, string Id, string ChangeType, string Version, DateTimeOffset Timestamp);

/// <summary>Clean, chunk, embed and upsert a changed source document, versioned (FR-C1).</summary>
public interface IKnowledgeIndexer
{
    Task UpsertAsync(SourceChange change, CancellationToken ct);
}
