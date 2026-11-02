using Assistant.Application.Abstractions;
using Assistant.Application.Chat;
using Assistant.Application.Ingestion;
using Assistant.Application.Tools;
using Assistant.Infrastructure.Azure;
using Assistant.Infrastructure.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the orchestrator and tool invoker (adapter-independent).</summary>
    public static IServiceCollection AddAssistantApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ChatOrchestrator>();
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
    /// Azure adapters. Guardrail, retrieval and model adapters are stubs that throw until implemented;
    /// the remaining ports still use in-memory placeholders and are marked as such in the stub file.
    /// </summary>
    public static IServiceCollection AddAzureAdapters(this IServiceCollection services)
    {
        services.AddSingleton<IInputGuardrail, ContentSafetyInputGuardrail>();
        services.AddSingleton<IOutputGuardrail, ContentSafetyOutputGuardrail>();
        services.AddSingleton<IRetriever, AzureAiSearchRetriever>();
        services.AddSingleton<IModelGateway, AzureOpenAiModelGateway>();

        // TODO: replace with Managed Redis, Cosmos DB, Service Bus/Dynamics and the immutable audit store.
        services.AddSingleton<InMemorySemanticCache>();
        services.AddSingleton<ISemanticCache>(sp => sp.GetRequiredService<InMemorySemanticCache>());
        services.AddSingleton<ICacheInvalidator>(sp => sp.GetRequiredService<InMemorySemanticCache>());
        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<IPendingActionStore, InMemoryPendingActionStore>();
        services.AddSingleton<IAuditLog, LoggingAuditLog>();
        services.AddSingleton<IEscalationService, LoggingEscalationService>();
        services.AddSingleton<IKnowledgeIndexer, LoggingKnowledgeIndexer>();
        return services;
    }
}
