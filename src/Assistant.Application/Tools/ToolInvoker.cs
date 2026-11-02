using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Assistant.Application.Abstractions;
using Assistant.Domain.Actions;
using Assistant.Domain.Identity;

namespace Assistant.Application.Tools;

public sealed record ToolResult(bool Success, string Summary);

/// <summary>
/// A typed commerce capability. Identity arrives as <see cref="UserContext"/> from the authenticated
/// session, never inside <c>arguments</c> (CLAUDE.md invariant 2). Implementations must also
/// re-enforce the customer identity at the backing API (FR-B5).
/// </summary>
public interface ITool
{
    string Name { get; }

    bool IsWrite { get; }

    /// <summary>Validates the arguments against the tool's schema and checks the caller may use it.</summary>
    Task<bool> IsAuthorizedAsync(UserContext user, JsonElement arguments, CancellationToken ct);

    /// <summary>For writes, <paramref name="idempotencyKey"/> is the confirmed action id.</summary>
    Task<ToolResult> ExecuteAsync(UserContext user, JsonElement arguments, string? idempotencyKey, CancellationToken ct);
}

public sealed record WriteProposal(string ConfirmationToken, string ToolName, DateTimeOffset ExpiresAt);

public sealed class ToolDeniedException(string message) : Exception(message);

/// <summary>
/// Enforces the tool rules from the spec: reads are authorized per user (FR-B2), writes follow
/// prepare then execute with a single-use confirmation token (FR-B4), and every write is audited.
/// </summary>
public sealed class ToolInvoker(
    IEnumerable<ITool> tools,
    IPendingActionStore pendingActions,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, ITool> _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public async Task<ToolResult> InvokeReadAsync(
        string requestId, string toolName, UserContext user, JsonElement arguments, CancellationToken ct)
    {
        var tool = Resolve(toolName, expectWrite: false);
        await EnsureAuthorizedAsync(tool, user, arguments, ct).ConfigureAwait(false);
        return await tool.ExecuteAsync(user, arguments, idempotencyKey: null, ct).ConfigureAwait(false);
    }

    /// <summary>Step 1 of a write: validates and stores a proposal; nothing is written yet.</summary>
    public async Task<WriteProposal> PrepareWriteAsync(
        string toolName, UserContext user, JsonElement arguments, CancellationToken ct)
    {
        var tool = Resolve(toolName, expectWrite: true);
        await EnsureAuthorizedAsync(tool, user, arguments, ct).ConfigureAwait(false);

        var action = new PendingAction(
            Guid.NewGuid().ToString("N"),
            user.UserId,
            tool.Name,
            HashArguments(arguments),
            clock.GetUtcNow() + ConfirmationLifetime);
        await pendingActions.SaveAsync(action, ct).ConfigureAwait(false);

        return new WriteProposal(action.Id, tool.Name, action.ExpiresAt);
    }

    /// <summary>Step 2 of a write: runs only if the user confirmed this exact action once.</summary>
    public async Task<ToolResult> ExecuteWriteAsync(
        string requestId,
        string confirmationToken,
        string toolName,
        UserContext user,
        JsonElement arguments,
        CancellationToken ct)
    {
        var tool = Resolve(toolName, expectWrite: true);
        await EnsureAuthorizedAsync(tool, user, arguments, ct).ConfigureAwait(false);

        var result = await pendingActions.TryConsumeAsync(
            confirmationToken, user.UserId, tool.Name, HashArguments(arguments), clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (result != ConfirmationResult.Confirmed)
        {
            await WriteAuditAsync(requestId, user, tool.Name, $"denied:{result}", ct).ConfigureAwait(false);
            throw new ToolDeniedException($"Write not confirmed: {result}");
        }

        var outcome = await tool.ExecuteAsync(user, arguments, confirmationToken, ct).ConfigureAwait(false);
        await WriteAuditAsync(requestId, user, tool.Name, outcome.Success ? "executed" : "failed", ct).ConfigureAwait(false);
        return outcome;
    }

    private ITool Resolve(string toolName, bool expectWrite)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            throw new ToolDeniedException($"Unknown tool: {toolName}");
        }

        if (tool.IsWrite != expectWrite)
        {
            throw new ToolDeniedException($"Tool {toolName} cannot be used this way");
        }

        return tool;
    }

    private static async Task EnsureAuthorizedAsync(ITool tool, UserContext user, JsonElement arguments, CancellationToken ct)
    {
        if (!await tool.IsAuthorizedAsync(user, arguments, ct).ConfigureAwait(false))
        {
            throw new ToolDeniedException($"Not authorized for tool {tool.Name}");
        }
    }

    // Raw JSON text is a stand-in for canonicalization; replace with schema-based canonical form when tools are defined.
    private static string HashArguments(JsonElement arguments) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.GetRawText())));

    private Task WriteAuditAsync(string requestId, UserContext user, string toolName, string outcome, CancellationToken ct) =>
        audit.WriteAsync(new AuditRecord(clock.GetUtcNow(), requestId, user.UserId, $"tool:{toolName}", outcome), ct);
}
