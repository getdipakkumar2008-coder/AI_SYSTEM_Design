// Governed AI shopping assistant: Azure infrastructure skeleton (resource-group scope).
// See architecture.md section 12 for the target design and infra/README.md for what is still TODO.

targetScope = 'resourceGroup'

@allowed(['dev', 'test', 'prod'])
param environmentName string

param location string = resourceGroup().location

@description('Region for the fallback Azure OpenAI account. Use a different region from the primary for resilience.')
param fallbackLocation string

@minLength(3)
@maxLength(8)
param namePrefix string = 'aiasst'

param tenantId string = tenant().tenantId

@description('Primary model deployment: { name, model, version, capacity }. Pin the version. The deployment name must equal the fallback name, because APIM fails over by backend and the URL carries the deployment name.')
param primaryModel object

@description('Fallback model deployment: { name, model, version, capacity }. Pin the version.')
param fallbackModel object

@description('Client id of the Entra app registration that represents this API (created outside Bicep).')
param apiAppClientId string

@description('Token scope the API uses to call the APIM gateway, for example api://<gateway-app-registration-id>/.default.')
param gatewayScope string

@allowed(['Developer', 'Premium', 'StandardV2', 'PremiumV2'])
param apimSku string = 'Developer'
param apimPublisherEmail string
param apimPublisherName string

@description('API container image. The default is a placeholder; CI/CD replaces it.')
param apiImage string = 'mcr.microsoft.com/k8se/quickstart:latest'
param apiMinReplicas int = 1

var tags = {
  workload: 'ai-shopping-assistant'
  environment: environmentName
}
var suffix = take(uniqueString(resourceGroup().id), 6)
var base = '${namePrefix}-${environmentName}'
var compact = '${namePrefix}${environmentName}${suffix}'

// Built-in Azure role definition ids (verified with `az role definition list`).
var roles = {
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  searchIndexDataReader: '1407120a-92aa-4202-b7e9-c0e197c71c8f'
  searchIndexDataContributor: '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
  cognitiveServicesOpenAiUser: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
  cognitiveServicesUser: 'a97b65f3-24c7-4388-baec-2e87135dc908'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  serviceBusDataSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: { name: base, location: location, tags: tags }
}

module network 'modules/network.bicep' = {
  name: 'network'
  params: { name: base, location: location, tags: tags }
}

// One identity per workload for least-privilege RBAC.
module apiIdentity 'modules/identity.bicep' = {
  name: 'id-api'
  params: { name: '${base}-api', location: location, tags: tags }
}

module ingestionIdentity 'modules/identity.bicep' = {
  name: 'id-ingestion'
  params: { name: '${base}-ingestion', location: location, tags: tags }
}

module gatewayIdentity 'modules/identity.bicep' = {
  name: 'id-gateway'
  params: { name: '${base}-gateway', location: location, tags: tags }
}

var zoneIds = {
  vault: resourceId('Microsoft.Network/privateDnsZones', network.outputs.zoneNames.vault)
  cosmos: resourceId('Microsoft.Network/privateDnsZones', network.outputs.zoneNames.cosmos)
  search: resourceId('Microsoft.Network/privateDnsZones', network.outputs.zoneNames.search)
  openai: resourceId('Microsoft.Network/privateDnsZones', network.outputs.zoneNames.openai)
  cognitive: resourceId('Microsoft.Network/privateDnsZones', network.outputs.zoneNames.cognitive)
  servicebus: resourceId('Microsoft.Network/privateDnsZones', network.outputs.zoneNames.servicebus)
  redis: resourceId('Microsoft.Network/privateDnsZones', network.outputs.zoneNames.redis)
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    name: 'kv-${compact}'
    location: location
    tags: tags
    tenantId: tenantId
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneIds: [zoneIds.vault]
    roleAssignments: [
      { principalId: apiIdentity.outputs.principalId, roleDefinitionId: roles.keyVaultSecretsUser }
    ]
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    name: 'cosmos-${compact}'
    location: location
    tags: tags
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneIds: [zoneIds.cosmos]
    dataContributorPrincipalIds: [apiIdentity.outputs.principalId]
  }
}

module redis 'modules/redis.bicep' = {
  name: 'redis'
  params: {
    name: 'redis-${base}-${suffix}'
    location: location
    tags: tags
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneIds: [zoneIds.redis]
    dataPrincipalIds: [apiIdentity.outputs.principalId, ingestionIdentity.outputs.principalId]
  }
}

module search 'modules/search.bicep' = {
  name: 'search'
  params: {
    name: 'srch-${compact}'
    location: location
    tags: tags
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneIds: [zoneIds.search]
    roleAssignments: [
      { principalId: apiIdentity.outputs.principalId, roleDefinitionId: roles.searchIndexDataReader }
      { principalId: ingestionIdentity.outputs.principalId, roleDefinitionId: roles.searchIndexDataContributor }
    ]
  }
}

var openAiRoleAssignments = [
  { principalId: gatewayIdentity.outputs.principalId, roleDefinitionId: roles.cognitiveServicesOpenAiUser }
]

module openAiPrimary 'modules/cognitive.bicep' = {
  name: 'openai-primary'
  params: {
    name: 'oai-${compact}-p'
    location: location
    tags: tags
    kind: 'OpenAI'
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneIds: [zoneIds.openai, zoneIds.cognitive]
    deployments: [primaryModel]
    roleAssignments: openAiRoleAssignments
  }
}

module openAiFallback 'modules/cognitive.bicep' = {
  name: 'openai-fallback'
  params: {
    name: 'oai-${compact}-f'
    location: fallbackLocation
    tags: tags
    kind: 'OpenAI'
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneIds: [zoneIds.openai, zoneIds.cognitive]
    deployments: [fallbackModel]
    roleAssignments: openAiRoleAssignments
  }
}

module contentSafety 'modules/cognitive.bicep' = {
  name: 'content-safety'
  params: {
    name: 'cs-${compact}'
    location: location
    tags: tags
    kind: 'ContentSafety'
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneIds: [zoneIds.cognitive]
    roleAssignments: [
      { principalId: apiIdentity.outputs.principalId, roleDefinitionId: roles.cognitiveServicesUser }
    ]
  }
}

// Azure AI Language: PII detection for the input and output guardrails.
module language 'modules/cognitive.bicep' = {
  name: 'language'
  params: {
    name: 'lang-${compact}'
    location: location
    tags: tags
    kind: 'TextAnalytics'
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneIds: [zoneIds.cognitive]
    roleAssignments: [
      { principalId: apiIdentity.outputs.principalId, roleDefinitionId: roles.cognitiveServicesUser }
    ]
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus'
  params: {
    name: 'sb-${compact}'
    location: location
    tags: tags
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneIds: [zoneIds.servicebus]
    roleAssignments: [
      { principalId: ingestionIdentity.outputs.principalId, roleDefinitionId: roles.serviceBusDataReceiver }
      { principalId: apiIdentity.outputs.principalId, roleDefinitionId: roles.serviceBusDataSender }
    ]
  }
}

module apim 'modules/apim.bicep' = {
  name: 'apim'
  params: {
    name: 'apim-${compact}'
    location: location
    tags: tags
    skuName: apimSku
    publisherEmail: apimPublisherEmail
    publisherName: apimPublisherName
    gatewayIdentityId: gatewayIdentity.outputs.id
    gatewayIdentityClientId: gatewayIdentity.outputs.clientId
    primaryOpenAiEndpoint: openAiPrimary.outputs.endpoint
    fallbackOpenAiEndpoint: openAiFallback.outputs.endpoint
    appInsightsId: monitoring.outputs.appInsightsId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
  }
}

// Non-secret configuration contract for the API (read via IConfiguration; double underscore = section separator).
module containerApps 'modules/containerapps.bicep' = {
  name: 'containerapps'
  params: {
    name: base
    location: location
    tags: tags
    infrastructureSubnetId: network.outputs.appSubnetId
    logAnalyticsWorkspaceName: monitoring.outputs.workspaceName
    userAssignedIdentityId: apiIdentity.outputs.id
    image: apiImage
    minReplicas: apiMinReplicas
    environmentVariables: [
      { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
      { name: 'AZURE_CLIENT_ID', value: apiIdentity.outputs.clientId }
      { name: 'AzureAd__TenantId', value: tenantId }
      { name: 'AzureAd__ClientId', value: apiAppClientId }
      { name: 'AzureAd__Audience', value: 'api://${apiAppClientId}' }
      { name: 'Assistant__UseDevAdapters', value: 'false' }
      { name: 'Assistant__Azure__CosmosEndpoint', value: cosmos.outputs.endpoint }
      { name: 'Assistant__Azure__SearchEndpoint', value: search.outputs.endpoint }
      { name: 'Assistant__Azure__ContentSafetyEndpoint', value: contentSafety.outputs.endpoint }
      { name: 'Assistant__Azure__LanguageEndpoint', value: language.outputs.endpoint }
      { name: 'Assistant__Azure__ModelGatewayUrl', value: apim.outputs.gatewayUrl }
      { name: 'Assistant__Azure__ModelGatewayScope', value: gatewayScope }
      { name: 'Assistant__Azure__ModelDeployment', value: primaryModel.name }
      { name: 'Assistant__Azure__ServiceBusNamespace', value: serviceBus.outputs.fullyQualifiedNamespace }
      { name: 'Assistant__Azure__RedisHost', value: redis.outputs.hostName }
      { name: 'Assistant__Azure__KeyVaultUri', value: keyVault.outputs.uri }
      { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: monitoring.outputs.appInsightsConnectionString }
    ]
  }
}

output apiFqdn string = containerApps.outputs.apiFqdn
output apimGatewayUrl string = apim.outputs.gatewayUrl
output serviceBusNamespace string = serviceBus.outputs.fullyQualifiedNamespace
output searchEndpoint string = search.outputs.endpoint
