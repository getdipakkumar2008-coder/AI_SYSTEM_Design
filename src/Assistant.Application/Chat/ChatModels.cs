using Assistant.Domain.Answers;

namespace Assistant.Application.Chat;

public sealed record ChatRequest(string RequestId, string SessionId, string Message);

public enum ChatOutcome
{
    Answered,
    AnsweredFromCache,
    Refused,
    Escalated,
}

public sealed record ChatResponse(
    string RequestId,
    ChatOutcome Outcome,
    string Text,
    IReadOnlyList<Citation> Citations,
    string? ReasonCode = null,
    string? TicketId = null)
{
    public static ChatResponse Refusal(string requestId, string reasonCode) =>
        new(requestId, ChatOutcome.Refused, RefusalMessages.For(reasonCode), [], reasonCode);
}

public static class RefusalMessages
{
    public static string For(string reasonCode) => reasonCode switch
    {
        ReasonCodes.BudgetExceeded => "That request took too long to process. Please try again or ask for a specialist.",
        ReasonCodes.Unavailable => "The assistant is temporarily unavailable. Please try again shortly.",
        _ => "Sorry, I cannot help with that request.",
    };
}

public static class ReasonCodes
{
    public const string InputBlocked = "input_blocked";
    public const string OutputBlocked = "output_blocked";
    public const string GuardrailError = "guardrail_error";
    public const string NoGrounding = "no_grounding";
    public const string BudgetExceeded = "budget_exceeded";
    public const string Unavailable = "unavailable";
    public const string LowConfidence = "low_confidence";
    public const string MessageTooLong = "message_too_long";
}
