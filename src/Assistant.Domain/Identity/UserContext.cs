namespace Assistant.Domain.Identity;

/// <summary>
/// Caller identity derived from the validated access token. It is never built from
/// model output or request-body fields (CLAUDE.md invariant 2).
/// </summary>
public sealed record UserContext(
    string UserId,
    string TenantId,
    IReadOnlySet<string> Groups,
    bool IsAnonymous = false)
{
    public static UserContext Anonymous { get; } =
        new("anonymous", "public", new HashSet<string>(), IsAnonymous: true);
}
