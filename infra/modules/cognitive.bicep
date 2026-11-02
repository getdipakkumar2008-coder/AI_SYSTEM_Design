param name string
param location string
param tags object
@description('OpenAI for Azure OpenAI in Foundry Models, ContentSafety for Azure AI Content Safety, TextAnalytics for Azure AI Language (PII detection).')
@allowed(['OpenAI', 'ContentSafety', 'TextAnalytics'])
param kind string
param privateEndpointSubnetId string
param privateDnsZoneIds array
@description('Model deployments: [{ name, model, version, capacity }]. Pin versions; changes go through evaluation gates.')
param deployments array = []
@description('Role assignments: [{ principalId, roleDefinitionId, principalType }]')
param roleAssignments array = []

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  tags: tags
  kind: kind
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: name
    disableLocalAuth: true // managed identity only; no API keys
    publicNetworkAccess: 'Disabled'
    networkAcls: { defaultAction: 'Deny' }
  }
}

@batchSize(1)
resource modelDeployments 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = [for d in deployments: {
  parent: account
  name: d.name
  sku: {
    name: 'Standard'
    capacity: d.capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: d.model
      version: d.version
    }
    versionUpgradeOption: 'NoAutoUpgrade' // never auto-upgrade; see CLAUDE.md model rules
  }
}]

resource roles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for r in roleAssignments: {
  scope: account
  name: guid(account.id, r.principalId, r.roleDefinitionId)
  properties: {
    principalId: r.principalId
    principalType: r.?principalType ?? 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', r.roleDefinitionId)
  }
}]

module pe 'privateEndpoint.bicep' = {
  name: 'pe-${name}'
  params: {
    name: 'pe-${name}'
    location: location
    tags: tags
    targetResourceId: account.id
    groupId: 'account'
    subnetId: privateEndpointSubnetId
    privateDnsZoneIds: privateDnsZoneIds
  }
  dependsOn: [modelDeployments]
}

output id string = account.id
output endpoint string = account.properties.endpoint
