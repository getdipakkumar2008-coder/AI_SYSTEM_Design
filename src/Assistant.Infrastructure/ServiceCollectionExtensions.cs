using Assistant.Application.Abstractions;
using Assistant.Application.Chat;
using Assistant.Application.Ingestion;
using Assistant.Application.Tools;
using Assistant.Infrastructure.AzureAdapters;
using Assistant.Infrastructure.Dev;
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Search.Documents;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Assistant.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the orchestrator and tool invoker (adapter-independent).</summary>
    public static IServiceCollection AddAssistantApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        // Transient: guardrail and model clients wrap IHttpClientFactory handlers, which should be resolved per request.
        services.AddTransient<ChatOrchestrator>();
        services.AddSingleton<ToolInvoker>();
        return services;
    }

    /// <summary>In-memory adapters for local development and tests. Never use in production.</summary>
    public static IServiceCollection AddDevAdapters(this IServiceCollection services)
    {
        services.AddSingleton<IInputGuardrail, DevInputGuardrail>();
        services.AddSingleton<IOutputGuardrail, DevOutputGuardrail>();
        services.AddSingleton<InMemorySemanticCache>();
        services.AddSingleton<ISemanticCache>(sp => sp.GetRequiredService<InMemorySemanticCache>());
        services.AddSingleton<ICacheInvalidator>(sp => sp.GetRequiredService<InMemorySemanticCache>());
        services.AddSingleton<IRetriever, DevRetriever>();
        services.AddSingleton<IModelGateway, DevModelGateway>();
        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<IPendingActionStore, InMemoryPendingActionStore>();
        services.AddSingleton<IAuditLog, LoggingAuditLog>();
        services.AddSingleton<IEscalationService, LoggingEscalationService>();
        services.AddSingleton<IKnowledgeIndexer, LoggingKnowledgeIndexer>();
        return services;
    }

    /// <summary>
    /// Azure adapters for the request path. Authentication is by managed identity (DefaultAzureCredential reads
    /// AZURE_CLIENT_ID for a user-assigned identity). Settings come from <c>Assistant:Azure</c>.
    /// Still placeholders: audit (writes to the application log, not an immutable store).
    /// </summary>
    public static IServiceCollection AddAzureAdapters(this IServiceCollection services)
    {
        services.AddAzureCore(requireRequestPath: true);

        services.AddHttpClient<ContentSafetyApi>((sp, client) => ConfigureClient(client, Options(sp).ContentSafetyEndpoint));
        services.AddHttpClient<LanguagePiiApi>((sp, client) => ConfigureClient(client, Options(sp).LanguageEndpoint));
        services.AddHttpClient<IModelGateway, AzureOpenAiModelGateway>((sp, client) => ConfigureClient(client, Options(sp).ModelGatewayUrl));
        services.AddTransient<IInputGuardrail, ContentSafetyInputGuardrail>();
        services.AddTransient<IOutputGuardrail, ContentSafetyOutputGuardrail>();

        services.AddSingleton(sp =>
        {
            var o = Options(sp);
            return new SearchClient(new Uri(o.SearchEndpoint), o.SearchIndex, sp.GetRequiredService<TokenCredential>());
        });
        services.AddSingleton<IRetriever>(sp => new AzureAiSearchRetriever(sp.GetRequiredService<SearchClient>(), Options(sp).SearchUseVectors));

        services.AddSingleton(sp =>
        {
            var o = Options(sp);
            return new CosmosClient(o.CosmosEndpoint, sp.GetRequiredService<TokenCredential>(), new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),
            });
        });
        services.AddSingleton<ISessionStore>(sp => new CosmosSessionStore(
            sp.GetRequiredService<CosmosClient>().GetContainer(Options(sp).CosmosDatabase, "sessions"),
            sp.GetRequiredService<IOptions<AzureAdapterOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IPendingActionStore>(sp => new CosmosPendingActionStore(
            sp.GetRequiredService<CosmosClient>().GetContainer(Options(sp).CosmosDatabase, "pendingActions")));

        services.AddRedisCache();

        services.AddSingleton(sp => new ServiceBusClient(Options(sp).ServiceBusNamespace, sp.GetRequiredService<TokenCredential>()));
        services.AddSingleton<IEscalationService, ServiceBusEscalationService>();

        // TODO: immutable audit store (Blob with an immutability policy). Until then audit records go to the app log.
        services.AddSingleton<IAuditLog, LoggingAuditLog>();
        services.AddSingleton<IKnowledgeIndexer, LoggingKnowledgeIndexer>();
        return services;
    }

    /// <summary>
    /// Adapters for the ingestion Function: cache invalidation on Redis. The knowledge indexer is still a logging
    /// placeholder because the source-document reader (commerce database) is not defined yet.
    /// </summary>
    public static IServiceCollection AddAzureIngestionAdapters(this IServiceCollection services)
    {
        services.AddAzureCore(requireRequestPath: false);
        services.AddRedisCache();
        services.AddSingleton<IKnowledgeIndexer, LoggingKnowledgeIndexer>();
        return services;
    }

    private static void AddAzureCore(this IServiceCollection services, bool requireRequestPath)
    {
        services.AddOptions<AzureAdapterOptions>()
            .BindConfiguration(AzureAdapterOptions.SectionName)
            .Validate(
                o => !requireRequestPath || AllSet(
                    o.CosmosEndpoint, o.SearchEndpoint, o.ContentSafetyEndpoint, o.LanguageEndpoint, o.ModelGatewayUrl,
                    o.ServiceBusNamespace),
                "Assistant:Azure is missing required endpoints (Cosmos, Search, ContentSafety, Language, ModelGatewayUrl, ServiceBusNamespace).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.RedisHost), "Assistant:Azure:RedisHost is required.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
    }

    private static void AddRedisCache(this IServiceCollection services)
    {
        services.AddSingleton<RedisConnection>();
        services.AddSingleton<RedisSemanticCache>();
        services.AddSingleton<ISemanticCache>(sp => sp.GetRequiredService<RedisSemanticCache>());
        services.AddSingleton<ICacheInvalidator>(sp => sp.GetRequiredService<RedisSemanticCache>());
    }

    private static AzureAdapterOptions Options(IServiceProvider sp) => sp.GetRequiredService<IOptions<AzureAdapterOptions>>().Value;

    private static void ConfigureClient(HttpClient client, string endpoint)
    {
        client.BaseAddress = new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/");
        client.Timeout = TimeSpan.FromSeconds(30); // the request budget enforces the real limit
    }

    private static bool AllSet(params string[] values) => values.All(v => !string.IsNullOrWhiteSpace(v));
}
