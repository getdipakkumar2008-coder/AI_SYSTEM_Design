using Assistant.Application.Abstractions;
using Assistant.Application.Chat;
using Assistant.Application.Tools;
using Assistant.Infrastructure;
using Assistant.Infrastructure.AzureAdapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.UnitTests.Infrastructure;

/// <summary>Proves the Azure composition root wires up and rejects incomplete configuration, without calling Azure.</summary>
public class AzureWiringTests
{
    private static readonly Dictionary<string, string?> FullConfig = new()
    {
        ["Assistant:Azure:CosmosEndpoint"] = "https://cosmos.test:443/",
        ["Assistant:Azure:SearchEndpoint"] = "https://search.test",
        ["Assistant:Azure:ContentSafetyEndpoint"] = "https://safety.test",
        ["Assistant:Azure:LanguageEndpoint"] = "https://lang.test",
        ["Assistant:Azure:ModelGatewayUrl"] = "https://apim.test",
        ["Assistant:Azure:ServiceBusNamespace"] = "sb.servicebus.windows.net",
        ["Assistant:Azure:RedisHost"] = "redis.test",
    };

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddLogging();
        services.AddAssistantApplication();
        services.AddAzureAdapters();
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task Request_path_graph_resolves_with_azure_adapters()
    {
        await using var provider = Build(FullConfig);

        Assert.NotNull(provider.GetRequiredService<ChatOrchestrator>());
        Assert.NotNull(provider.GetRequiredService<ToolInvoker>());
        Assert.IsType<ContentSafetyInputGuardrail>(provider.GetRequiredService<IInputGuardrail>());
        Assert.IsType<ContentSafetyOutputGuardrail>(provider.GetRequiredService<IOutputGuardrail>());
        Assert.IsType<AzureAiSearchRetriever>(provider.GetRequiredService<IRetriever>());
        Assert.IsType<AzureOpenAiModelGateway>(provider.GetRequiredService<IModelGateway>());
        Assert.IsType<CosmosSessionStore>(provider.GetRequiredService<ISessionStore>());
        Assert.IsType<CosmosPendingActionStore>(provider.GetRequiredService<IPendingActionStore>());
        Assert.IsType<RedisSemanticCache>(provider.GetRequiredService<ISemanticCache>());
        Assert.IsType<ServiceBusEscalationService>(provider.GetRequiredService<IEscalationService>());
    }

    [Fact]
    public void Cache_and_invalidator_are_the_same_instance()
    {
        using var provider = Build(FullConfig);

        Assert.Same(provider.GetRequiredService<ISemanticCache>(), provider.GetRequiredService<ICacheInvalidator>());
    }

    [Theory]
    [InlineData("Assistant:Azure:SearchEndpoint")]
    [InlineData("Assistant:Azure:ContentSafetyEndpoint")]
    [InlineData("Assistant:Azure:LanguageEndpoint")]
    [InlineData("Assistant:Azure:ModelGatewayUrl")]
    [InlineData("Assistant:Azure:RedisHost")]
    public void Missing_required_setting_fails_options_validation(string missingKey)
    {
        var values = new Dictionary<string, string?>(FullConfig);
        values.Remove(missingKey);
        using var provider = Build(values);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AzureAdapterOptions>>().Value);
    }

    [Fact]
    public void Ingestion_graph_needs_only_redis_and_resolves_the_invalidator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Assistant:Azure:RedisHost"] = "redis.test" }).Build());
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddAzureIngestionAdapters();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.IsType<RedisSemanticCache>(provider.GetRequiredService<ICacheInvalidator>());
    }
}
