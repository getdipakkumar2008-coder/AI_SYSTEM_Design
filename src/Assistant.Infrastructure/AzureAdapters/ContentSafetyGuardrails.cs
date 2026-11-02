using Assistant.Application.Abstractions;
using Assistant.Domain.Answers;
using Assistant.Domain.Guardrails;
using Assistant.Domain.Identity;
using Microsoft.Extensions.Options;

namespace Assistant.Infrastructure.AzureAdapters;

/// <summary>
/// FR-A3: scope check, Prompt Shields, harm categories, PII redaction. Any service error throws, and the
/// orchestrator treats that as a block (fail closed).
/// </summary>
public sealed class ContentSafetyInputGuardrail(
    ContentSafetyApi contentSafety,
    LanguagePiiApi pii,
    IOptions<AzureAdapterOptions> options) : IInputGuardrail
{
    private readonly AzureAdapterOptions _options = options.Value;

    public async Task<InputCheckResult> CheckAsync(string message, UserContext user, CancellationToken ct)
    {
        var trimmed = message.Trim();
        if (trimmed.Length == 0)
        {
            return new InputCheckResult(false, string.Empty, "empty_message");
        }

        if (await contentSafety.IsPromptAttackAsync(trimmed, ct).ConfigureAwait(false))
        {
            return new InputCheckResult(false, string.Empty, "injection_suspected");
        }

        if (await contentSafety.MaxHarmSeverityAsync(trimmed, ct).ConfigureAwait(false) >= _options.BlockHarmSeverityAtOrAbove)
        {
            return new InputCheckResult(false, string.Empty, "unsafe_input");
        }

        if (!_options.RedactPii)
        {
            return new InputCheckResult(true, trimmed);
        }

        var redacted = await pii.DetectAsync(trimmed, _options.PiiLanguage, ct).ConfigureAwait(false);
        return new InputCheckResult(true, redacted.RedactedText);
    }
}

/// <summary>
/// FR-A7: citation check, harm categories, PII leak check, optional groundedness detection.
/// Deterministic checks run first so cheap failures skip the service calls.
/// </summary>
public sealed class ContentSafetyOutputGuardrail(
    ContentSafetyApi contentSafety,
    LanguagePiiApi pii,
    IOptions<AzureAdapterOptions> options) : IOutputGuardrail
{
    private const int MaxGroundingChars = 50_000;

    private readonly AzureAdapterOptions _options = options.Value;

    public async Task<GuardrailVerdict> CheckAsync(
        DraftAnswer draft, IReadOnlyList<RetrievedPassage> evidence, UserContext user, CancellationToken ct)
    {
        if (!HasValidCitations(draft, evidence))
        {
            return GuardrailVerdict.Block("ungrounded");
        }

        if (await contentSafety.MaxHarmSeverityAsync(draft.Text, ct).ConfigureAwait(false) >= _options.BlockHarmSeverityAtOrAbove)
        {
            return GuardrailVerdict.Block("unsafe_output");
        }

        var detected = await pii.DetectAsync(draft.Text, _options.PiiLanguage, ct).ConfigureAwait(false);
        if (LeaksPii(detected.EntityTexts, evidence))
        {
            return GuardrailVerdict.Block("pii_leak");
        }

        if (_options.GroundednessDetectionEnabled)
        {
            var sources = LimitSources(evidence);
            if (await contentSafety.IsUngroundedAsync(draft.Text, sources, ct).ConfigureAwait(false))
            {
                return GuardrailVerdict.Block("ungrounded");
            }
        }

        return GuardrailVerdict.Allow;
    }

    /// <summary>A factual answer needs at least one citation, and every citation must refer to retrieved evidence.</summary>
    internal static bool HasValidCitations(DraftAnswer draft, IReadOnlyList<RetrievedPassage> evidence) =>
        draft.Citations.Count > 0
        && draft.Citations.All(c => evidence.Any(e => string.Equals(e.SourceId, c.SourceId, StringComparison.Ordinal)));

    /// <summary>PII in the answer is allowed only if it already appears in the evidence the user is entitled to see.</summary>
    internal static bool LeaksPii(IReadOnlyList<string> entityTexts, IReadOnlyList<RetrievedPassage> evidence) =>
        entityTexts.Any(entity =>
            !evidence.Any(e => e.Text.Contains(entity, StringComparison.OrdinalIgnoreCase)));

    private static List<string> LimitSources(IReadOnlyList<RetrievedPassage> evidence)
    {
        var sources = new List<string>();
        var total = 0;
        foreach (var passage in evidence)
        {
            if (total + passage.Text.Length > MaxGroundingChars)
            {
                break;
            }

            sources.Add(passage.Text);
            total += passage.Text.Length;
        }

        return sources;
    }
}
