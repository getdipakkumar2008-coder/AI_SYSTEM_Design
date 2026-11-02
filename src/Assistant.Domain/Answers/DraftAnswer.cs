namespace Assistant.Domain.Answers;

/// <summary>A model-produced answer that has NOT yet passed output guardrails.</summary>
public sealed record DraftAnswer(string Text, IReadOnlyList<Citation> Citations, bool UsedPersonalData);
