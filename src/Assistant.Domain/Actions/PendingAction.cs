namespace Assistant.Domain.Actions;

public enum ConfirmationResult
{
    Confirmed,
    NotFound,
    WrongUser,
    WrongAction,
    Expired,
    AlreadyUsed,
}

/// <summary>
/// A proposed write awaiting user confirmation (FR-B4). The id doubles as the single-use
/// confirmation token and as the idempotency key for the eventual write.
/// </summary>
public sealed class PendingAction
{
    private int _consumed;

    public PendingAction(string id, string userId, string toolName, string argumentsHash, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        ToolName = toolName;
        ArgumentsHash = argumentsHash;
        ExpiresAt = expiresAt;
    }

    public string Id { get; }

    public string UserId { get; }

    public string ToolName { get; }

    public string ArgumentsHash { get; }

    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Atomically consumes the token if it matches the caller, tool and arguments and is unexpired.
    /// Persistent stores must provide the same atomicity (for example an ETag precondition).
    /// </summary>
    public ConfirmationResult TryConsume(string userId, string toolName, string argumentsHash, DateTimeOffset now)
    {
        if (!string.Equals(userId, UserId, StringComparison.Ordinal))
        {
            return ConfirmationResult.WrongUser;
        }

        if (!string.Equals(toolName, ToolName, StringComparison.Ordinal)
            || !string.Equals(argumentsHash, ArgumentsHash, StringComparison.Ordinal))
        {
            return ConfirmationResult.WrongAction;
        }

        if (now >= ExpiresAt)
        {
            return ConfirmationResult.Expired;
        }

        return Interlocked.CompareExchange(ref _consumed, 1, 0) == 0
            ? ConfirmationResult.Confirmed
            : ConfirmationResult.AlreadyUsed;
    }
}
