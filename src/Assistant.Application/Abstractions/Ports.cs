using Assistant.Domain.Actions;
using Assistant.Domain.Answers;
using Assistant.Domain.Budgets;
using Assistant.Domain.Guardrails;
using Assistant.Domain.Identity;

namespace Assistant.Application.Abstractions;

public sealed record InputCheckResult(bool Allowed, string SanitizedMessage, string? ReasonCode = null);

/// <summary>Injection, PII and scope checks (FR-A3). Implementations fail closed.</summary>
public interface IInputGuardrail
{
    Task<InputCheckResult> CheckAsync(string message, UserContext user, CancellationToken ct);
}

/// <summary>Safety, privacy, grounding and policy checks on the full draft (FR-A7).</summary>
public interface IOutputGuardrail
{
    Task<GuardrailVerdict> CheckAsync(
        DraftAnswer draft,
        IReadOnlyList<RetrievedPassage> evidence,
        UserContext user,
        CancellationToken ct);
}

/// <summary>
/// Screens retrieved passages for indirect prompt injection before they reach the model (FR-B10).
/// Returns only the passages that are safe to use; implementations fail closed by throwing.
/// </summary>
public interface IEvidenceGuardrail
{
    Task<IReadOnlyList<RetrievedPassage>> ScreenAsync(
        string userMessage, IReadOnlyList<RetrievedPassage> evidence, CancellationToken ct);
}

public sealed record CachedAnswer(
    DraftAnswer Answer,
    IReadOnlyList<RetrievedPassage> Evidence,
    string Version,
    DateTimeOffset StoredAt);

/// <summary>Holds validated, public, non-personalized answers only (FR-A4).</summary>
public interface ISemanticCache
{
    Task<CachedAnswer?> TryGetAsync(string sanitizedMessage, CancellationToken ct);

    Task SetAsync(string sanitizedMessage, CachedAnswer entry, CancellationToken ct);
}

/// <summary>Invalidates cache entries when source data changes (FR-C2).</summary>
public interface ICacheInvalidator
{
    /// <param name="sourceKey">The <c>entity:id</c> key of the changed source (see <c>SourceChange.SourceKey</c>).</param>
    Task InvalidateAsync(string sourceKey, CancellationToken ct);
}

/// <summary>
/// Hybrid search + rerank. Implementations MUST filter by <c>user.Groups</c> before ranking (FR-B1).
/// <see cref="RetrievedPassage.SourceId"/> MUST be the <c>entity:id</c> key of the source record (for example
/// <c>product:42</c>) so cache invalidation can match it to change events.
/// </summary>
public interface IRetriever
{
    Task<IReadOnlyList<RetrievedPassage>> SearchAsync(string query, UserContext user, int top, CancellationToken ct);
}

public sealed record Turn(string Role, string Text);

public sealed record ModelRequest(
    string SystemInstructions,
    string UserMessage,
    IReadOnlyList<RetrievedPassage> Evidence,
    IReadOnlyList<Turn> History);

/// <param name="Answerable">False when the model says the passages do not contain the answer. That is a
/// grounding failure to escalate, not an answer to validate.</param>
public sealed record ModelResponse(
    string Text,
    IReadOnlyList<string> CitedSourceIds,
    double Confidence,
    string ServedBy,
    int TokensUsed,
    bool Answerable = true);

/// <summary>Model routing, retries, timeouts and primary/fallback failover (FR-B6, FR-B7).</summary>
public interface IModelGateway
{
    Task<ModelResponse> GenerateAsync(ModelRequest request, RequestBudget budget, CancellationToken ct);
}

public sealed class ModelUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record HandoffPackage(
    string UserId,
    string Reason,
    IReadOnlyList<Turn> Conversation,
    IReadOnlyList<RetrievedPassage> Evidence,
    string? ProposedAction);

/// <summary>Creates a human-support ticket and returns its id (FR-B8, FR-B9).</summary>
public interface IEscalationService
{
    Task<string> EscalateAsync(HandoffPackage package, CancellationToken ct);
}

public sealed record AuditRecord(
    DateTimeOffset At,
    string RequestId,
    string UserId,
    string Action,
    string Outcome,
    IReadOnlyDictionary<string, string>? Details = null);

/// <summary>Append-only audit trail. Never include raw PII in <see cref="AuditRecord.Details"/>.</summary>
public interface IAuditLog
{
    Task WriteAsync(AuditRecord record, CancellationToken ct);
}

public interface ISessionStore
{
    Task<IReadOnlyList<Turn>> GetRecentAsync(string userId, string sessionId, int max, CancellationToken ct);

    Task AppendAsync(string userId, string sessionId, Turn turn, CancellationToken ct);
}

public interface IPendingActionStore
{
    Task SaveAsync(PendingAction action, CancellationToken ct);

    /// <summary>Atomically validates and consumes a confirmation token. Single use.</summary>
    Task<ConfirmationResult> TryConsumeAsync(
        string actionId, string userId, string toolName, string argumentsHash, DateTimeOffset now, CancellationToken ct);
}
