using System.Text.Json;
using Assistant.Application.Tools;
using Assistant.Infrastructure.Dev;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.UnitTests.Application;

/// <summary>Maps to FR-B2, FR-B4 and SPEC.md acceptance criteria AC-2 and AC-4.</summary>
public class ToolInvokerTests
{
    private static readonly JsonElement Args = JsonDocument.Parse("""{"orderId":"123"}""").RootElement;

    private static (ToolInvoker Invoker, FakeTool Write, RecordingAudit Audit, FakeTimeProvider Clock) Create(bool writeAuthorized = true)
    {
        var read = new FakeTool("get_order", isWrite: false);
        var write = new FakeTool("execute_return", isWrite: true, authorized: writeAuthorized);
        var audit = new RecordingAudit();
        var clock = new FakeTimeProvider();
        return (new ToolInvoker([read, write], new InMemoryPendingActionStore(), audit, clock), write, audit, clock);
    }

    [Fact]
    public async Task Prepare_alone_never_executes_the_write()
    {
        var (invoker, write, _, _) = Create();

        await invoker.PrepareWriteAsync("execute_return", Users.Alice, Args, CancellationToken.None);

        Assert.Equal(0, write.Executions);
    }

    [Fact] // AC-4
    public async Task Confirmed_write_executes_once_with_token_as_idempotency_key_and_is_audited()
    {
        var (invoker, write, audit, _) = Create();
        var proposal = await invoker.PrepareWriteAsync("execute_return", Users.Alice, Args, CancellationToken.None);

        await invoker.ExecuteWriteAsync("r1", proposal.ConfirmationToken, "execute_return", Users.Alice, Args, CancellationToken.None);

        Assert.Equal(1, write.Executions);
        Assert.Equal(proposal.ConfirmationToken, write.LastIdempotencyKey);
        Assert.Contains(audit.Records, r => r.Action == "tool:execute_return" && r.Outcome == "executed");
    }

    [Fact] // AC-4
    public async Task Replaying_a_token_is_rejected()
    {
        var (invoker, write, _, _) = Create();
        var proposal = await invoker.PrepareWriteAsync("execute_return", Users.Alice, Args, CancellationToken.None);
        await invoker.ExecuteWriteAsync("r1", proposal.ConfirmationToken, "execute_return", Users.Alice, Args, CancellationToken.None);

        await Assert.ThrowsAsync<ToolDeniedException>(() =>
            invoker.ExecuteWriteAsync("r2", proposal.ConfirmationToken, "execute_return", Users.Alice, Args, CancellationToken.None));

        Assert.Equal(1, write.Executions);
    }

    [Fact] // AC-2
    public async Task Another_users_token_is_rejected()
    {
        var (invoker, write, _, _) = Create();
        var proposal = await invoker.PrepareWriteAsync("execute_return", Users.Alice, Args, CancellationToken.None);

        await Assert.ThrowsAsync<ToolDeniedException>(() =>
            invoker.ExecuteWriteAsync("r1", proposal.ConfirmationToken, "execute_return", Users.Bob, Args, CancellationToken.None));

        Assert.Equal(0, write.Executions);
    }

    [Fact]
    public async Task Changed_arguments_after_confirmation_are_rejected()
    {
        var (invoker, write, _, _) = Create();
        var proposal = await invoker.PrepareWriteAsync("execute_return", Users.Alice, Args, CancellationToken.None);
        var tampered = JsonDocument.Parse("""{"orderId":"999"}""").RootElement;

        await Assert.ThrowsAsync<ToolDeniedException>(() =>
            invoker.ExecuteWriteAsync("r1", proposal.ConfirmationToken, "execute_return", Users.Alice, tampered, CancellationToken.None));

        Assert.Equal(0, write.Executions);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var (invoker, write, _, clock) = Create();
        var proposal = await invoker.PrepareWriteAsync("execute_return", Users.Alice, Args, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(10));

        await Assert.ThrowsAsync<ToolDeniedException>(() =>
            invoker.ExecuteWriteAsync("r1", proposal.ConfirmationToken, "execute_return", Users.Alice, Args, CancellationToken.None));

        Assert.Equal(0, write.Executions);
    }

    [Fact] // AC-2
    public async Task Unauthorized_user_cannot_prepare_a_write()
    {
        var (invoker, _, _, _) = Create(writeAuthorized: false);

        await Assert.ThrowsAsync<ToolDeniedException>(() =>
            invoker.PrepareWriteAsync("execute_return", Users.Alice, Args, CancellationToken.None));
    }

    [Fact]
    public async Task Write_tool_cannot_be_called_through_the_read_path()
    {
        var (invoker, write, _, _) = Create();

        await Assert.ThrowsAsync<ToolDeniedException>(() =>
            invoker.InvokeReadAsync("r1", "execute_return", Users.Alice, Args, CancellationToken.None));

        Assert.Equal(0, write.Executions);
    }

    [Fact]
    public async Task Unknown_tool_is_denied()
    {
        var (invoker, _, _, _) = Create();

        await Assert.ThrowsAsync<ToolDeniedException>(() =>
            invoker.InvokeReadAsync("r1", "drop_database", Users.Alice, Args, CancellationToken.None));
    }
}
