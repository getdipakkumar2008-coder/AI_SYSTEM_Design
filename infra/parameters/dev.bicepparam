using '../main.bicep'

param environmentName = 'dev'

// Pick a second region that has Azure OpenAI capacity for your model; it should differ from the resource group region.
param fallbackLocation = 'swedencentral'

// Example model/version only. Confirm availability and quota in your regions, choose a current model,
// and pin the version. Changing a model or version is a release gated by the evaluation suite.
param primaryModel = {
  name: 'chat-primary'
  model: 'gpt-4o-mini'
  version: '2024-07-18'
  capacity: 30
}

param fallbackModel = {
  name: 'chat-fallback'
  model: 'gpt-4o-mini'
  version: '2024-07-18'
  capacity: 30
}

param apimSku = 'Developer'
param apimPublisherEmail = 'platform-team@example.com'
param apimPublisherName = 'AI Platform Team'
