namespace Assistant.Domain.Budgets;

public enum BudgetKind
{
    Steps,
    Tokens,
    Time,
}

public sealed class BudgetExceededException(BudgetKind kind)
    : Exception($"Request budget exceeded: {kind}")
{
    public BudgetKind Kind { get; } = kind;
}
