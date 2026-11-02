using '../main.bicep'

param environmentName = 'dev'

// Pick a second region that has Azure OpenAI capacity for your model; it should differ from the resource group region.
param fallbackLocation = 'swedencentral'

// Example model/version only. Confirm availability and quota in your regions, choose a current model,
// and pin the version. Changing a model or version is a release gated by the evaluation suite.
param primaryModel = {
  name: 'chat'
  model: 'gpt-4o-mini'
  version: '2024-07-18'
  capacity: 30
}

param fallbackModel = {
  name: 'chat'
  model: 'gpt-4o-mini'
  version: '2024-07-18'
  capacity: 30
}

// Entra app registration for the API itself (created outside Bicep).
param apiAppClientId = '<api-app-registration-client-id>'

// Token audience of the APIM gateway app registration (created outside Bicep).
param gatewayScope = 'api://<gateway-app-registration-id>/.default'

param apimSku = 'Developer'
param apimPublisherEmail = 'platform-team@example.com'
param apimPublisherName = 'AI Platform Team'
