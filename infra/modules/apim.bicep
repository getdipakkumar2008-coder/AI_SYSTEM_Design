param name string
param location string
param tags object
@allowed(['Developer', 'Premium', 'StandardV2', 'PremiumV2'])
param skuName string = 'Developer'
param publisherEmail string
param publisherName string
@description('Resource id of the user-assigned identity that has the Azure OpenAI User role on both accounts.')
param gatewayIdentityId string
param gatewayIdentityClientId string
param primaryOpenAiEndpoint string
param fallbackOpenAiEndpoint string
param appInsightsId string
param appInsightsConnectionString string

resource apim 'Microsoft.ApiManagement/service@2023-05-01-preview' = {
  name: name
  location: location
  tags: tags
  sku: { name: skuName, capacity: 1 }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${gatewayIdentityId}': {} }
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
    // TODO(network): VNet integration or private access, then restrict the public endpoint (Front Door is the public entry).
    customProperties: {
      // Disable legacy protocols and ciphers.
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Tls10': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Tls11': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Ssl30': 'false'
    }
  }
}

resource identityClientId 'Microsoft.ApiManagement/service/namedValues@2023-05-01-preview' = {
  parent: apim
  name: 'gateway-identity-client-id'
  properties: {
    displayName: 'gateway-identity-client-id'
    value: gatewayIdentityClientId
  }
}

resource primaryBackend 'Microsoft.ApiManagement/service/backends@2023-05-01-preview' = {
  parent: apim
  name: 'openai-primary'
  properties: {
    protocol: 'http'
    url: '${primaryOpenAiEndpoint}openai'
  }
}

resource fallbackBackend 'Microsoft.ApiManagement/service/backends@2023-05-01-preview' = {
  parent: apim
  name: 'openai-fallback'
  properties: {
    protocol: 'http'
    url: '${fallbackOpenAiEndpoint}openai'
  }
}

resource openAiApi 'Microsoft.ApiManagement/service/apis@2023-05-01-preview' = {
  parent: apim
  name: 'azure-openai'
  properties: {
    displayName: 'Azure OpenAI gateway'
    path: 'openai'
    protocols: ['https']
    subscriptionRequired: true
    // TODO: import the Azure OpenAI OpenAPI spec (format: openapi+json-link) so operations are explicit.
    serviceUrl: '${primaryOpenAiEndpoint}openai'
  }
}

resource openAiPolicy 'Microsoft.ApiManagement/service/apis/policies@2023-05-01-preview' = {
  parent: openAiApi
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: loadTextContent('../policies/azure-openai-gateway.xml')
  }
  dependsOn: [primaryBackend, fallbackBackend, identityClientId]
}

resource logger 'Microsoft.ApiManagement/service/loggers@2023-05-01-preview' = {
  parent: apim
  name: 'appinsights'
  properties: {
    loggerType: 'applicationInsights'
    resourceId: appInsightsId
    credentials: { connectionString: appInsightsConnectionString }
  }
}

output gatewayUrl string = apim.properties.gatewayUrl
output id string = apim.id
