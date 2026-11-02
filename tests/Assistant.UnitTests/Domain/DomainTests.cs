using Assistant.Domain.Actions;
using Assistant.Domain.Budgets;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.UnitTests.Domain;

public class RequestBudgetTests
{
    private static RequestBudget Create(FakeTimeProvider clock, int steps = 3, int tokens = 100) =>
        new(steps, tokens, TimeSpan.FromSeconds(5), clock);

    [Fact]
    public void ConsumeStep_beyond_limit_throws_steps()
    {
        var budget = Create(new FakeTimeProvider(), steps: 2);
        budget.ConsumeStep();
        budget.ConsumeStep();

        var ex = Assert.Throws<BudgetExceededException>(budget.ConsumeStep);
        Assert.Equal(BudgetKind.Steps, ex.Kind);
    }

    [Fact]
    public void ConsumeTokens_beyond_limit_throws_tokens()
    {
        var budget = Create(new FakeTimeProvider(), tokens: 10);
        var ex = Assert.Throws<BudgetExceededException>(() => budget.ConsumeTokens(11));
        Assert.Equal(BudgetKind.Tokens, ex.Kind);
    }

    [Fact]
    public void ConsumeStep_after_deadline_throws_time()
    {
        var clock = new FakeTimeProvider();
        var budget = Create(clock);
        clock.Advance(TimeSpan.FromSeconds(6));

        var ex = Assert.Throws<BudgetExceededException>(budget.ConsumeStep);
        Assert.Equal(BudgetKind.Time, ex.Kind);
    }
}

public class PendingActionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PendingAction Create() => new("a1", "user-1", "execute_return", "hash", Now.AddMinutes(5));

    [Fact]
    public void Matching_caller_consumes_once()
    {
        var action = Create();
        Assert.Equal(ConfirmationResult.Confirmed, action.TryConsume("user-1", "execute_return", "hash", Now));
        Assert.Equal(ConfirmationResult.AlreadyUsed, action.TryConsume("user-1", "execute_return", "hash", Now));
    }

    [Fact]
    public void Other_user_cannot_consume_and_does_not_burn_the_token()
    {
        var action = Create();
        Assert.Equal(ConfirmationResult.WrongUser, action.TryConsume("user-2", "execute_return", "hash", Now));
        Assert.Equal(ConfirmationResult.Confirmed, action.TryConsume("user-1", "execute_return", "hash", Now));
    }

    [Fact]
    public void Changed_arguments_are_rejected()
    {
        Assert.Equal(ConfirmationResult.WrongAction, Create().TryConsume("user-1", "execute_return", "other", Now));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        Assert.Equal(ConfirmationResult.Expired, Create().TryConsume("user-1", "execute_return", "hash", Now.AddMinutes(6)));
    }

    [Fact]
    public async Task Concurrent_consumption_confirms_exactly_once()
    {
        var action = Create();
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(() => action.TryConsume("user-1", "execute_return", "hash", Now))));

        Assert.Equal(1, results.Count(r => r == ConfirmationResult.Confirmed));
    }
}
