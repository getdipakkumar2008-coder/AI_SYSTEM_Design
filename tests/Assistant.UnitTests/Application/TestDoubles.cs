using System.Text.Json;
using Assistant.Application.Abstractions;
using Assistant.Application.Chat;
using Assistant.Application.Tools;
using Assistant.Domain.Answers;
using Assistant.Domain.Budgets;
using Assistant.Domain.Guardrails;
using Assistant.Domain.Identity;
using Assistant.Infrastructure.Dev;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.UnitTests.Application;

internal static class Users
{
    public static UserContext Alice { get; } = new("alice", "t1", new HashSet<string>());

    public static UserContext Bob { get; } = new("bob", "t1", new HashSet<string>());
}

internal sealed class FakeInputGuardrail : IInputGuardrail
{
    public bool Allow { get; set; } = true;

    public bool Throw { get; set; }

    public int Calls { get; private set; }

    public Task<InputCheckResult> CheckAsync(string message, UserContext user, CancellationToken ct)
    {
        Calls++;
        if (Throw)
        {
            throw new InvalidOperationException("guardrail down");
        }

        return Task.FromResult(new InputCheckResult(Allow, message, Allow ? null : "injection_suspected"));
    }
}

internal sealed class FakeOutputGuardrail : IOutputGuardrail
{
    public bool Allow { get; set; } = true;

    public int Calls { get; private set; }

    public Task<GuardrailVerdict> CheckAsync(
        DraftAnswer draft, IReadOnlyList<RetrievedPassage> evidence, UserContext user, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Allow ? GuardrailVerdict.Allow : GuardrailVerdict.Block("ungrounded"));
    }
}

internal sealed class FakeRetriever : IRetriever
{
    public List<RetrievedPassage> Passages { get; } = [new("doc1", "Policy", "Returns in 30 days.", IsPublic: true)];

    public int Calls { get; private set; }

    public Task<IReadOnlyList<RetrievedPassage>> SearchAsync(string query, UserContext user, int top, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<RetrievedPassage>>(Passages.ToList());
    }
}

internal sealed class FakeModelGateway : IModelGateway
{
    public double Confidence { get; set; } = 0.9;

    public bool Unavailable { get; set; }

    public int Calls { get; private set; }

    public Task<ModelResponse> GenerateAsync(ModelRequest request, RequestBudget budget, CancellationToken ct)
    {
        Calls++;
        return Unavailable
            ? throw new ModelUnavailableException("down")
            : Task.FromResult(new ModelResponse("Returns are accepted within 30 days.", ["doc1"], Confidence, "fake", 100));
    }
}

internal sealed class RecordingEscalation : IEscalationService
{
    public List<HandoffPackage> Packages { get; } = [];

    public Task<string> EscalateAsync(HandoffPackage package, CancellationToken ct)
    {
        Packages.Add(package);
        return Task.FromResult("T-1");
    }
}

internal sealed class RecordingAudit : IAuditLog
{
    public List<AuditRecord> Records { get; } = [];

    public Task WriteAsync(AuditRecord record, CancellationToken ct)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }
}

/// <summary>Wires a <see cref="ChatOrchestrator"/> with fakes so tests can assert on collaborators.</summary>
internal sealed class ChatHarness
{
    public ChatHarness()
    {
        Orchestrator = new ChatOrchestrator(
            Input, Output, Cache, Retriever, Model, Sessions, Escalation, Audit, Clock,
            Options.Create(new ChatOptions()), NullLogger<ChatOrchestrator>.Instance);
    }

    public FakeInputGuardrail Input { get; } = new();

    public FakeOutputGuardrail Output { get; } = new();

    public InMemorySemanticCache Cache { get; } = new();

    public FakeRetriever Retriever { get; } = new();

    public FakeModelGateway Model { get; } = new();

    public InMemorySessionStore Sessions { get; } = new();

    public RecordingEscalation Escalation { get; } = new();

    public RecordingAudit Audit { get; } = new();

    public FakeTimeProvider Clock { get; } = new();

    public ChatOrchestrator Orchestrator { get; }

    public Task<ChatResponse> AskAsync(string message = "What is the returns policy?", UserContext? user = null) =>
        Orchestrator.HandleAsync(new ChatRequest(Guid.NewGuid().ToString("N"), "s1", message), user ?? Users.Alice, CancellationToken.None);
}

internal sealed class FakeTool(string name, bool isWrite, bool authorized = true) : ITool
{
    public string Name { get; } = name;

    public bool IsWrite { get; } = isWrite;

    public int Executions { get; private set; }

    public string? LastIdempotencyKey { get; private set; }

    public Task<bool> IsAuthorizedAsync(UserContext user, JsonElement arguments, CancellationToken ct) => Task.FromResult(authorized);

    public Task<ToolResult> ExecuteAsync(UserContext user, JsonElement arguments, string? idempotencyKey, CancellationToken ct)
    {
        Executions++;
        LastIdempotencyKey = idempotencyKey;
        return Task.FromResult(new ToolResult(true, "ok"));
    }
}
