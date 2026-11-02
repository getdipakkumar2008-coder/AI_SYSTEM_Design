namespace Assistant.Domain.Guardrails;

public sealed record GuardrailVerdict(bool Allowed, string? ReasonCode = null)
{
    public static GuardrailVerdict Allow { get; } = new(true);

    public static GuardrailVerdict Block(string reasonCode) => new(false, reasonCode);
}
