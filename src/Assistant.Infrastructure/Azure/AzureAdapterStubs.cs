using Assistant.Application.Abstractions;
using Assistant.Domain.Answers;
using Assistant.Domain.Budgets;
using Assistant.Domain.Guardrails;
using Assistant.Domain.Identity;

namespace Assistant.Infrastructure.Azure;

// Placeholders for the real Azure adapters (architecture.md section 12). Each throws until implemented.
// The orchestrator fails closed on guardrail exceptions, so an unimplemented guardrail can never let content through.
// Authenticate with DefaultAzureCredential / managed identity; do not add keys or connection-string secrets.

/// <summary>TODO(FR-A3): Azure AI Content Safety Prompt Shields + Azure AI Language PII detection.</summary>
public sealed class ContentSafetyInputGuardrail : IInputGuardrail
{
    public Task<InputCheckResult> CheckAsync(string message, UserContext user, CancellationToken ct) =>
        throw new NotImplementedException("Implement with Azure AI Content Safety and Azure AI Language.");
}

/// <summary>TODO(FR-A7): Content Safety harm categories + groundedness detection + citation verification.</summary>
public sealed class ContentSafetyOutputGuardrail : IOutputGuardrail
{
    public Task<GuardrailVerdict> CheckAsync(
        DraftAnswer draft, IReadOnlyList<RetrievedPassage> evidence, UserContext user, CancellationToken ct) =>
        throw new NotImplementedException("Implement with Azure AI Content Safety groundedness detection.");
}

/// <summary>TODO(FR-B1): Azure AI Search hybrid query with semantic ranker; filter on ACL group ids from <c>user.Groups</c>.</summary>
public sealed class AzureAiSearchRetriever : IRetriever
{
    public Task<IReadOnlyList<RetrievedPassage>> SearchAsync(string query, UserContext user, int top, CancellationToken ct) =>
        throw new NotImplementedException("Implement with Azure.Search.Documents and a security-trimming filter.");
}

/// <summary>TODO(FR-B6, FR-B7): call Azure OpenAI through the APIM AI gateway; primary and fallback deployments.</summary>
public sealed class AzureOpenAiModelGateway : IModelGateway
{
    public Task<ModelResponse> GenerateAsync(ModelRequest request, RequestBudget budget, CancellationToken ct) =>
        throw new NotImplementedException("Implement with the Azure OpenAI client behind API Management.");
}

// Still to add (see docs): Managed Redis semantic cache, Cosmos DB session and pending-action stores,
// Dynamics 365 / Service Bus escalation, Application Insights audit sink + immutable audit store, AI Search indexer.
