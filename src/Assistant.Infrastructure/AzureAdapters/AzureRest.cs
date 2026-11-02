using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;

namespace Assistant.Infrastructure.AzureAdapters;

internal sealed record RestResult(int StatusCode, JsonElement Body, string? ServedBy)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>Throws without including the response body, which may echo user content.</summary>
    public void EnsureSuccess(string operation)
    {
        if (!IsSuccess)
        {
            throw new HttpRequestException($"{operation} failed with status {StatusCode}", null, (System.Net.HttpStatusCode)StatusCode);
        }
    }
}

/// <summary>Shared helper for calling Azure REST endpoints with a bearer token from the managed identity.</summary>
internal static class AzureRest
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<RestResult> PostJsonAsync(
        HttpClient http,
        TokenCredential credential,
        string scope,
        string relativeUri,
        object body,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUri)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        var token = await credential.GetTokenAsync(new TokenRequestContext([scope]), ct).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);

        var servedBy = response.Headers.TryGetValues("x-served-by", out var values) ? values.FirstOrDefault() : null;
        JsonElement parsed = default;
        if (response.IsSuccessStatusCode)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            parsed = doc.RootElement.Clone();
        }

        return new RestResult((int)response.StatusCode, parsed, servedBy);
    }
}
