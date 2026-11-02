using System.Security.Claims;
using System.Threading.RateLimiting;
using Assistant.Api;
using Assistant.Application.Chat;
using Assistant.Infrastructure;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// Identity: Microsoft Entra ID bearer tokens. Every endpoint requires an authenticated caller by default.
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Per-user rate limiting (FR-A2). Partition key is the authenticated user, never a request field.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirstValue("oid") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("Assistant:RateLimit:PermitsPerMinute", 30),
                Window = TimeSpan.FromMinutes(1),
            }));
});

builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection(ChatOptions.SectionName));
builder.Services.AddAssistantApplication();
if (builder.Configuration.GetValue<bool>("Assistant:UseDevAdapters"))
{
    builder.Services.AddDevAdapters();
}
else
{
    builder.Services.AddAzureAdapters();
}

builder.Services.AddHealthChecks();

// Telemetry (FR-D1). Enabled when an Application Insights connection string is configured.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.MapPost("/v1/chat", async (ChatRequestDto body, ClaimsPrincipal principal, ChatOrchestrator orchestrator, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Message))
    {
        return Results.Problem("Message is required.", statusCode: StatusCodes.Status400BadRequest);
    }

    var request = new ChatRequest(Guid.NewGuid().ToString("N"), body.SessionId ?? "default", body.Message);
    var response = await orchestrator.HandleAsync(request, principal.ToUserContext(), ct);
    return Results.Ok(response);
});

app.Run();

namespace Assistant.Api
{
    public sealed record ChatRequestDto(string Message, string? SessionId);

    public partial class Program;
}
