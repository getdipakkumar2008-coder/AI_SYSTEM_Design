using Assistant.Application.Abstractions;
using Assistant.Domain.Answers;
using Assistant.Domain.Guardrails;
using Assistant.Domain.Identity;
using Microsoft.Extensions.Logging;
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

        return new InputCheckResult(true, await pii.RedactAsync(trimmed, ct).ConfigureAwait(false));
    }
}

/// <summary>
/// FR-B10: screens retrieved passages with Prompt Shields document analysis, so a poisoned catalog or FAQ chunk
/// cannot inject instructions into the model. Flagged passages are dropped and logged by source id only.
/// </summary>
public sealed partial class ContentSafetyEvidenceGuardrail(ContentSafetyApi contentSafety, ILogger<ContentSafetyEvidenceGuardrail> logger)
    : IEvidenceGuardrail
{
    public async Task<IReadOnlyList<RetrievedPassage>> ScreenAsync(
        string userMessage, IReadOnlyList<RetrievedPassage> evidence, CancellationToken ct)
    {
        if (evidence.Count == 0)
        {
            return evidence;
        }

        var flags = await contentSafety.DetectDocumentAttacksAsync(userMessage, evidence.Select(p => p.Text).ToList(), ct).ConfigureAwait(false);

        var safe = new List<RetrievedPassage>();
        var dropped = new List<string>();
        for (var i = 0; i < evidence.Count; i++)
        {
            if (flags[i])
            {
                dropped.Add(evidence[i].SourceId);
            }
            else
            {
                safe.Add(evidence[i]);
            }
        }

        if (dropped.Count > 0)
        {
            LogDropped(dropped.Count, string.Join(",", dropped));
        }

        return safe;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped {Count} retrieved passage(s) flagged as prompt injection: {SourceIds}")]
    private partial void LogDropped(int count, string sourceIds);
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

        var entities = await pii.FindEntitiesAsync(draft.Text, ct).ConfigureAwait(false);
        if (LeaksPii(entities, evidence))
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
