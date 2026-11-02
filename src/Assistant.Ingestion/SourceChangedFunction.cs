using System.Text.Json;
using Assistant.Application.Abstractions;
using Assistant.Application.Ingestion;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Assistant.Ingestion;

/// <summary>
/// Consumes CDC events (SPEC.md section 6): refreshes the index, then invalidates affected cache entries (FR-C2).
/// Throwing lets Service Bus retry and eventually dead-letter the message (FR-C3).
/// Connection uses managed identity via the <c>ServiceBus__fullyQualifiedNamespace</c> app setting.
/// </summary>
public sealed partial class SourceChangedFunction(
    IKnowledgeIndexer indexer,
    ICacheInvalidator cacheInvalidator,
    ILogger<SourceChangedFunction> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function(nameof(SourceChangedFunction))]
    public async Task RunAsync(
        [ServiceBusTrigger("source-changes", Connection = "ServiceBus")] ServiceBusReceivedMessage message,
        CancellationToken ct)
    {
        var change = message.Body.ToObjectFromJson<SourceChange>(JsonOptions)
            ?? throw new InvalidOperationException("Empty source-change message.");

        LogProcessing(change.Entity, change.Id, change.Version);

        // Index first, then invalidate: a stale cache entry is worse than a briefly missing one.
        await indexer.UpsertAsync(change, ct);
        await cacheInvalidator.InvalidateAsync(change.Entity, change.Id, ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing source change {Entity}/{Id} version {Version}")]
    private partial void LogProcessing(string entity, string id, string version);
}
