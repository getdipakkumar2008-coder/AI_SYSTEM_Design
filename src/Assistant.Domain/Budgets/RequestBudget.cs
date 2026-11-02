namespace Assistant.Domain.Budgets;

/// <summary>
/// Per-request limits on workflow steps, model tokens and wall-clock time (FR-A6, FR-B6).
/// One instance per request; not shared across threads.
/// </summary>
public sealed class RequestBudget
{
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset _deadline;
    private int _steps;
    private int _tokens;

    public RequestBudget(int maxSteps, int maxTokens, TimeSpan timeout, TimeProvider clock)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSteps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        MaxSteps = maxSteps;
        MaxTokens = maxTokens;
        _clock = clock;
        _deadline = clock.GetUtcNow() + timeout;
    }

    public int MaxSteps { get; }

    public int MaxTokens { get; }

    public int TokensRemaining => Math.Max(0, MaxTokens - _tokens);

    public TimeSpan TimeRemaining => _deadline - _clock.GetUtcNow();

    /// <summary>Call before each workflow step. Throws when steps or time are exhausted.</summary>
    public void ConsumeStep()
    {
        EnsureTime();
        if (++_steps > MaxSteps)
        {
            throw new BudgetExceededException(BudgetKind.Steps);
        }
    }

    public void ConsumeTokens(int tokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        _tokens += tokens;
        if (_tokens > MaxTokens)
        {
            throw new BudgetExceededException(BudgetKind.Tokens);
        }
    }

    public void EnsureTime()
    {
        if (TimeRemaining <= TimeSpan.Zero)
        {
            throw new BudgetExceededException(BudgetKind.Time);
        }
    }
}
