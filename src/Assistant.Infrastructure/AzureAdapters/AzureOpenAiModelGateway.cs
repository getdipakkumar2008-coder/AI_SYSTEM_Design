using System.Net;
using System.Text;
using System.Text.Json;
using Assistant.Application.Abstractions;
using Assistant.Domain.Answers;
using Assistant.Domain.Budgets;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Assistant.Infrastructure.AzureAdapters;

/// <summary>
/// Calls Azure OpenAI chat completions through the API Management gateway (FR-B6, FR-B7). The gateway owns model
/// credentials, retries and primary-to-fallback failover (infra/policies/azure-openai-gateway.xml); this client
/// only enforces the per-request time and token budget and maps failures to <see cref="ModelUnavailableException"/>.
/// <paramref name="http"/> must have <c>BaseAddress</c> set to the gateway URL.
/// </summary>
public sealed class AzureOpenAiModelGateway(
    HttpClient http,
    TokenCredential credential,
    IOptions<AzureAdapterOptions> options) : IModelGateway
{
    private readonly AzureAdapterOptions _options = options.Value;

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, RequestBudget budget, CancellationToken ct)
    {
        budget.EnsureTime();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(budget.TimeRemaining);

        RestResult result;
        try
        {
            result = await AzureRest.PostJsonAsync(
                http,
                credential,
                _options.ModelGatewayScope,
                $"openai/deployments/{Uri.EscapeDataString(_options.ModelDeployment)}/chat/completions?api-version={_options.ModelApiVersion}",
                BuildBody(request, Math.Min(_options.ModelMaxOutputTokens, budget.TokensRemaining)),
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BudgetExceededException(BudgetKind.Time);
        }
        catch (HttpRequestException ex)
        {
            throw new ModelUnavailableException("Model gateway request failed.", ex);
        }

        if (!result.IsSuccess)
        {
            // 429 and 5xx remain after the gateway's own retry and fallback; anything else is a configuration fault.
            throw new ModelUnavailableException($"Model gateway returned status {result.StatusCode}.");
        }

        return Parse(result, request);
    }

    internal static object BuildBody(ModelRequest request, int maxTokens)
    {
        var messages = new List<object>
        {
            new { role = "system", content = request.SystemInstructions + OutputFormatInstructions },
        };
        messages.AddRange(request.History.Select(t => (object)new { role = MapRole(t.Role), content = t.Text }));
        messages.Add(new { role = "user", content = BuildUserMessage(request) });

        return new
        {
            messages,
            temperature = 0,
            max_tokens = maxTokens,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "grounded_answer",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "answer", "citedSourceIds", "confidence" },
                        properties = new
                        {
                            answer = new { type = "string" },
                            citedSourceIds = new { type = "array", items = new { type = "string" } },
                            confidence = new { type = "number" },
                        },
                    },
                },
            },
        };
    }

    /// <summary>
    /// Passages are untrusted data. Angle brackets are escaped so a passage cannot close its own tag and
    /// smuggle in instructions that look like part of the prompt structure.
    /// </summary>
    internal static string BuildUserMessage(ModelRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<passages>");
        foreach (var passage in request.Evidence)
        {
            sb.Append("<passage id=\"").Append(EscapeAttribute(passage.SourceId)).AppendLine("\">");
            sb.AppendLine(Escape(passage.Text));
            sb.AppendLine("</passage>");
        }

        sb.AppendLine("</passages>");
        sb.Append("<question>").Append(Escape(request.UserMessage)).Append("</question>");
        return sb.ToString();
    }

    private const string OutputFormatInstructions =
        " Respond with JSON: answer (string), citedSourceIds (ids of the passages you used), confidence (0 to 1).";

    private static string MapRole(string role) => role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";

    private static string Escape(string text) => text.Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    private static string EscapeAttribute(string text) => Escape(text).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static ModelResponse Parse(RestResult result, ModelRequest request)
    {
        var servedBy = result.ServedBy ?? "unknown";
        var tokens = result.Body.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var total)
            ? total.GetInt32()
            : 0;

        var choice = result.Body.GetProperty("choices")[0];
        var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
        if (finishReason is not "stop")
        {
            // content_filter, length or anything unexpected: do not trust the text; low confidence escalates.
            return new ModelResponse(string.Empty, [], Confidence: 0, servedBy, tokens);
        }

        try
        {
            using var content = JsonDocument.Parse(choice.GetProperty("message").GetProperty("content").GetString() ?? "{}");
            var root = content.RootElement;
            var knownIds = request.Evidence.Select(p => p.SourceId).ToHashSet(StringComparer.Ordinal);
            var cited = root.GetProperty("citedSourceIds").EnumerateArray()
                .Select(e => e.GetString())
                .Where(id => id is not null && knownIds.Contains(id))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var confidence = Math.Clamp(root.GetProperty("confidence").GetDouble(), 0, 1);

            return new ModelResponse(root.GetProperty("answer").GetString() ?? string.Empty, cited, confidence, servedBy, tokens);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new ModelResponse(string.Empty, [], Confidence: 0, servedBy, tokens);
        }
    }
}
