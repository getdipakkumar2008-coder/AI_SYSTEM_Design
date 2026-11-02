using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Assistant.Application.Abstractions;
using Azure.Core;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Assistant.Infrastructure.AzureAdapters;

/// <summary>Lazily opens one Azure Managed Redis connection authenticated with Microsoft Entra ID (no access keys).</summary>
public sealed class RedisConnection(IOptions<AzureAdapterOptions> options, TokenCredential credential)
{
    private readonly Lazy<Task<IConnectionMultiplexer>> _connection = new(() => ConnectAsync(options.Value, credential));

    public async Task<IDatabase> GetDatabaseAsync() => (await _connection.Value.ConfigureAwait(false)).GetDatabase();

    private static async Task<IConnectionMultiplexer> ConnectAsync(AzureAdapterOptions o, TokenCredential credential)
    {
        var config = new ConfigurationOptions
        {
            EndPoints = { { o.RedisHost, o.RedisPort } },
            Ssl = true,
            AbortOnConnectFail = false,
        };
        await config.ConfigureForAzureWithTokenCredentialAsync(credential).ConfigureAwait(false);
        return await ConnectionMultiplexer.ConnectAsync(config).ConfigureAwait(false);
    }
}

/// <summary>
/// Answer cache for public, validated, non-personalized answers (FR-A4).
/// This is an exact-match cache on the normalized question. It does not yet do similarity (embedding) matching;
/// that needs RediSearch vector queries plus an embedding call and is a follow-up.
/// Entries are tagged with the source ids of their evidence so a CDC event can invalidate them (FR-C2).
/// </summary>
public sealed partial class RedisSemanticCache(RedisConnection connection, IOptions<AzureAdapterOptions> options)
    : ISemanticCache, ICacheInvalidator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan TagGrace = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _ttl = options.Value.CacheTtl;

    public async Task<CachedAnswer?> TryGetAsync(string sanitizedMessage, CancellationToken ct)
    {
        var db = await connection.GetDatabaseAsync().ConfigureAwait(false);
        var value = await db.StringGetAsync(EntryKey(sanitizedMessage)).ConfigureAwait(false);
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CachedAnswer>((string)value!, Json);
    }

    public async Task SetAsync(string sanitizedMessage, CachedAnswer entry, CancellationToken ct)
    {
        var db = await connection.GetDatabaseAsync().ConfigureAwait(false);
        var key = EntryKey(sanitizedMessage);
        await db.StringSetAsync(key, JsonSerializer.Serialize(entry, Json), _ttl).ConfigureAwait(false);

        foreach (var sourceId in entry.Evidence.Select(e => e.SourceId).Distinct(StringComparer.Ordinal))
        {
            var tag = TagKey(sourceId);
            await db.SetAddAsync(tag, key).ConfigureAwait(false);
            await db.KeyExpireAsync(tag, _ttl + TagGrace).ConfigureAwait(false);
        }
    }

    /// <summary>Removes every cached answer that used the changed source. <paramref name="id"/> is the source id of the passages.</summary>
    public async Task InvalidateAsync(string entity, string id, CancellationToken ct)
    {
        var db = await connection.GetDatabaseAsync().ConfigureAwait(false);
        var tag = TagKey(id);
        var members = await db.SetMembersAsync(tag).ConfigureAwait(false);
        if (members.Length > 0)
        {
            await db.KeyDeleteAsync(members.Select(m => (RedisKey)(string)m!).ToArray()).ConfigureAwait(false);
        }

        await db.KeyDeleteAsync(tag).ConfigureAwait(false);
    }

    internal static string EntryKey(string message) => $"cache:{Hash(Normalize(message))}";

    internal static string TagKey(string sourceId) => $"tag:{sourceId}";

    internal static string Normalize(string message) =>
        Whitespace().Replace(message.Trim(), " ").ToLowerInvariant();

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
