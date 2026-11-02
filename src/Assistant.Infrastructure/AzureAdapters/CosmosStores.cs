using System.Net;
using Assistant.Application.Abstractions;
using Assistant.Domain.Actions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Assistant.Infrastructure.AzureAdapters;

/// <summary>
/// Conversation state in Cosmos DB (FR-C4). The partition key is the authenticated user id and every query
/// supplies it, so one user's turns can never be returned to another. Items expire with a per-item ttl.
/// </summary>
public sealed class CosmosSessionStore(Container container, IOptions<AzureAdapterOptions> options, TimeProvider clock) : ISessionStore
{
    private readonly int _ttlSeconds = (int)options.Value.SessionTtl.TotalSeconds;

    internal sealed record TurnDocument(
        string Id, string UserId, string SessionId, string Role, string Text, DateTimeOffset CreatedAt, int Ttl);

    public async Task<IReadOnlyList<Turn>> GetRecentAsync(string userId, string sessionId, int max, CancellationToken ct)
    {
        var query = new QueryDefinition(
                "SELECT TOP @max c.role, c.text FROM c WHERE c.userId = @userId AND c.sessionId = @sessionId ORDER BY c.createdAt DESC")
            .WithParameter("@max", max)
            .WithParameter("@userId", userId)
            .WithParameter("@sessionId", sessionId);

        var turns = new List<Turn>();
        using var iterator = container.GetItemQueryIterator<Turn>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });
        while (iterator.HasMoreResults)
        {
            turns.AddRange(await iterator.ReadNextAsync(ct).ConfigureAwait(false));
        }

        turns.Reverse(); // oldest first
        return turns;
    }

    public async Task AppendAsync(string userId, string sessionId, Turn turn, CancellationToken ct)
    {
        var document = new TurnDocument(
            Guid.NewGuid().ToString("N"), userId, sessionId, turn.Role, turn.Text, clock.GetUtcNow(), _ttlSeconds);
        await container.CreateItemAsync(document, new PartitionKey(userId), cancellationToken: ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Single-use confirmation tokens for write actions (FR-B4). Consumption is made atomic with an ETag precondition:
/// of two concurrent confirmations, only one replace succeeds and the other gets 412 and is reported as already used.
/// </summary>
public sealed class CosmosPendingActionStore(Container container, TimeProvider clock) : IPendingActionStore
{
    private const int TtlMarginSeconds = 300;
    private const int MinTtlSeconds = 60;

    internal sealed record PendingActionDocument(
        string Id, string UserId, string ToolName, string ArgumentsHash, DateTimeOffset ExpiresAt, bool Consumed, int Ttl);

    public async Task SaveAsync(PendingAction action, CancellationToken ct)
    {
        var document = new PendingActionDocument(
            action.Id, action.UserId, action.ToolName, action.ArgumentsHash, action.ExpiresAt, Consumed: false,
            TtlFor(action.ExpiresAt, clock.GetUtcNow()));
        await container.CreateItemAsync(document, new PartitionKey(action.UserId), cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Lifetime follows the token's own expiry plus a margin, so a replay is still recognised until it expires.</summary>
    internal static int TtlFor(DateTimeOffset expiresAt, DateTimeOffset now) =>
        (int)Math.Max(MinTtlSeconds, Math.Ceiling((expiresAt - now).TotalSeconds) + TtlMarginSeconds);

    public async Task<ConfirmationResult> TryConsumeAsync(
        string actionId, string userId, string toolName, string argumentsHash, DateTimeOffset now, CancellationToken ct)
    {
        // Reading with the caller's id as partition key means another user's token is simply not found.
        ItemResponse<PendingActionDocument> read;
        try
        {
            read = await container.ReadItemAsync<PendingActionDocument>(actionId, new PartitionKey(userId), cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return ConfirmationResult.NotFound;
        }

        var document = read.Resource;
        var validation = new PendingAction(document.Id, document.UserId, document.ToolName, document.ArgumentsHash, document.ExpiresAt)
            .TryConsume(userId, toolName, argumentsHash, now);
        if (validation != ConfirmationResult.Confirmed)
        {
            return validation; // a mismatched or expired attempt never consumes the token
        }

        if (document.Consumed)
        {
            return ConfirmationResult.AlreadyUsed;
        }

        try
        {
            await container.ReplaceItemAsync(
                document with { Consumed = true, Ttl = TtlFor(document.ExpiresAt, now) }, // a replace must not extend the lifetime
                document.Id,
                new PartitionKey(userId),
                new ItemRequestOptions { IfMatchEtag = read.ETag },
                ct).ConfigureAwait(false);
            return ConfirmationResult.Confirmed;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return ConfirmationResult.AlreadyUsed;
        }
    }
}
