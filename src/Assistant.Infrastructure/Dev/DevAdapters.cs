using System.Collections.Concurrent;
using Assistant.Application.Abstractions;
using Assistant.Application.Ingestion;
using Assistant.Domain.Actions;
using Assistant.Domain.Answers;
using Assistant.Domain.Guardrails;
using Assistant.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Assistant.Infrastructure.Dev;

// In-memory, single-process adapters for local development and tests ONLY.
// They are not safe for production: no persistence, no cross-instance sharing, naive guardrails.

public sealed class DevInputGuardrail : IInputGuardrail
{
    private static readonly string[] Suspicious =
    [
        "ignore previous", "ignore your rules", "ignore all instructions", "system prompt", "reveal your instructions",
    ];

    public Task<InputCheckResult> CheckAsync(string message, UserContext user, CancellationToken ct)
    {
        var blocked = Suspicious.Any(s => message.Contains(s, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(blocked
            ? new InputCheckResult(false, string.Empty, "injection_suspected")
            : new InputCheckResult(true, message.Trim()));
    }
}

public sealed class DevOutputGuardrail : IOutputGuardrail
{
    public Task<GuardrailVerdict> CheckAsync(
        DraftAnswer draft, IReadOnlyList<RetrievedPassage> evidence, UserContext user, CancellationToken ct)
    {
        // Grounding: a factual answer needs at least one citation that exists in the evidence.
        var grounded = draft.Citations.Count > 0
            && draft.Citations.All(c => evidence.Any(e => e.SourceId == c.SourceId));
        return Task.FromResult(grounded ? GuardrailVerdict.Allow : GuardrailVerdict.Block("ungrounded"));
    }
}

public sealed class InMemorySemanticCache : ISemanticCache, ICacheInvalidator
{
    private readonly ConcurrentDictionary<string, CachedAnswer> _entries = new(StringComparer.OrdinalIgnoreCase);

    public Task<CachedAnswer?> TryGetAsync(string sanitizedMessage, CancellationToken ct) =>
        Task.FromResult(_entries.GetValueOrDefault(sanitizedMessage));

    public Task SetAsync(string sanitizedMessage, CachedAnswer entry, CancellationToken ct)
    {
        _entries[sanitizedMessage] = entry;
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string entity, string id, CancellationToken ct)
    {
        foreach (var (key, entry) in _entries)
        {
            if (entry.Evidence.Any(e => e.SourceId == id))
            {
                _entries.TryRemove(key, out _);
            }
        }

        return Task.CompletedTask;
    }
}

public sealed class DevRetriever : IRetriever
{
    private static readonly RetrievedPassage[] Corpus =
    [
        new("faq-returns", "Returns policy", "Items can be returned within 30 days of delivery in original condition.", IsPublic: true),
        new("faq-shipping", "Shipping", "Standard shipping takes 3 to 5 business days.", IsPublic: true),
    ];

    public Task<IReadOnlyList<RetrievedPassage>> SearchAsync(string query, UserContext user, int top, CancellationToken ct)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IReadOnlyList<RetrievedPassage> hits = Corpus
            .Where(p => p.IsPublic || user.Groups.Contains("internal"))
            .Where(p => terms.Any(t => t.Length > 3 && (p.Text.Contains(t, StringComparison.OrdinalIgnoreCase)
                || p.Title.Contains(t, StringComparison.OrdinalIgnoreCase))))
            .Take(top)
            .ToList();
        return Task.FromResult(hits);
    }
}

public sealed class DevModelGateway : IModelGateway
{
    public Task<ModelResponse> GenerateAsync(ModelRequest request, Domain.Budgets.RequestBudget budget, CancellationToken ct)
    {
        var top = request.Evidence[0];
        return Task.FromResult(new ModelResponse(top.Text, [top.SourceId], Confidence: 0.9, ServedBy: "dev", TokensUsed: 50));
    }
}

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, List<Turn>> _sessions = new();

    public Task<IReadOnlyList<Turn>> GetRecentAsync(string userId, string sessionId, int max, CancellationToken ct)
    {
        // The key always includes the authenticated user id, so sessions cannot cross users.
        if (!_sessions.TryGetValue(Key(userId, sessionId), out var turns))
        {
            return Task.FromResult<IReadOnlyList<Turn>>([]);
        }

        lock (turns)
        {
            return Task.FromResult<IReadOnlyList<Turn>>(turns.TakeLast(max).ToList());
        }
    }

    public Task AppendAsync(string userId, string sessionId, Turn turn, CancellationToken ct)
    {
        var turns = _sessions.GetOrAdd(Key(userId, sessionId), _ => []);
        lock (turns)
        {
            turns.Add(turn);
        }

        return Task.CompletedTask;
    }

    private static string Key(string userId, string sessionId) => $"{userId}:{sessionId}";
}

public sealed class InMemoryPendingActionStore : IPendingActionStore
{
    private readonly ConcurrentDictionary<string, PendingAction> _actions = new();

    public Task SaveAsync(PendingAction action, CancellationToken ct)
    {
        _actions[action.Id] = action;
        return Task.CompletedTask;
    }

    public Task<ConfirmationResult> TryConsumeAsync(
        string actionId, string userId, string toolName, string argumentsHash, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(_actions.TryGetValue(actionId, out var action)
            ? action.TryConsume(userId, toolName, argumentsHash, now)
            : ConfirmationResult.NotFound);
}

public sealed partial class LoggingAuditLog(ILogger<LoggingAuditLog> logger) : IAuditLog
{
    public Task WriteAsync(AuditRecord record, CancellationToken ct)
    {
        LogAudit(record.RequestId, record.UserId, record.Action, record.Outcome);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AUDIT request={RequestId} user={UserId} action={Action} outcome={Outcome}")]
    private partial void LogAudit(string requestId, string userId, string action, string outcome);
}

public sealed partial class LoggingEscalationService(ILogger<LoggingEscalationService> logger) : IEscalationService
{
    public Task<string> EscalateAsync(HandoffPackage package, CancellationToken ct)
    {
        var ticket = $"DEV-{Guid.NewGuid():N}"[..12];
        LogEscalation(ticket, package.Reason);
        return Task.FromResult(ticket);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ESCALATION ticket={Ticket} reason={Reason}")]
    private partial void LogEscalation(string ticket, string reason);
}

public sealed partial class LoggingKnowledgeIndexer(ILogger<LoggingKnowledgeIndexer> logger) : IKnowledgeIndexer
{
    public Task UpsertAsync(SourceChange change, CancellationToken ct)
    {
        LogUpsert(change.Entity, change.Id, change.Version);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "INDEX upsert entity={Entity} id={Id} version={Version}")]
    private partial void LogUpsert(string entity, string id, string version);
}
