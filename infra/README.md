# Infrastructure (Bicep)

Skeleton for the Azure deployment in `architecture.md` section 12. It compiles and lints clean with Bicep 0.48; it has **not been deployed**, so expect to adjust quotas, regions, SKUs, and API versions on first deployment.

## Layout

```
infra/
  main.bicep                  resource-group scope; wires everything together
  parameters/dev.bicepparam   example parameters (model names/versions are examples)
  policies/azure-openai-gateway.xml   APIM policy: managed identity auth, retry, primary -> fallback
  modules/
    monitoring.bicep          Log Analytics + Application Insights (Entra auth)
    network.bicep             VNet, subnets, private DNS zones + links
    privateEndpoint.bicep     reusable private endpoint + DNS zone group
    identity.bicep            user-assigned managed identity (api, ingestion, gateway)
    keyvault.bicep            RBAC, purge protection, private only
    cosmos.bicep              sessions (ttl) + pendingActions containers, Entra data roles, no keys
    redis.bicep               Azure Managed Redis (semantic cache), Entra access, no keys
    search.bicep              Azure AI Search with semantic ranker, Entra only
    cognitive.bicep           Azure OpenAI (pinned model deployments) or Content Safety
    servicebus.bicep          Premium namespace + source-changes queue (CDC) with dead-lettering
    apim.bicep                API Management AI gateway in front of two OpenAI accounts
    containerapps.bicep       Container Apps environment (VNet-internal) + API app
```

## Security defaults

- Local/key authentication is disabled everywhere it exists; access is by managed identity and RBAC.
- Public network access is disabled on data and AI services; access is through Private Link.
- Each workload has its own identity with least privilege (api, ingestion, gateway).
- Model deployments never auto-upgrade; versions are pinned.

## Commands

```bash
az bicep build --file infra/main.bicep --stdout > /dev/null        # compile
az deployment group what-if -g <rg> -p infra/parameters/dev.bicepparam   # preview
az deployment group create  -g <rg> -p infra/parameters/dev.bicepparam   # deploy
```

## Not done yet (TODO)

| Area | Gap |
|---|---|
| Edge | Azure Front Door + WAF policy, and restricting APIM/Container Apps to Front Door only |
| APIM | VNet integration or private access; JWT validation of the calling identity; per-user token limit policy; import the Azure OpenAI API spec |
| Network | NSGs and egress restrictions on both subnets |
| Ingestion host | Function App hosting for `Assistant.Ingestion` (plan, storage with identity-based connections, VNet integration) |
| Audit | Immutable audit store (Blob with immutability policy) and Log Analytics retention/export policy |
| Identity | Entra app registrations (API, gateway audience) are created outside Bicep |
| Escalation | Dynamics 365 Customer Service / helpdesk integration |
| Resilience | Zone redundancy, second region, backup and DR settings for production (marked `TODO(prod)`) |
| Operations | Alert rules, dashboards, Azure Load Testing resource, Defender for Cloud / Purview |
| CI/CD | Pipeline that builds the images, runs `what-if`, deploys, and canary-shifts Container Apps revisions |
