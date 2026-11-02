param name string
param location string
param tags object
param privateEndpointSubnetId string
param privateDnsZoneIds array
@allowed(['basic', 'standard', 'standard2', 'standard3'])
param skuName string = 'standard'
param replicaCount int = 1
@description('Role assignments: [{ principalId, roleDefinitionId, principalType }]')
param roleAssignments array = []

resource search 'Microsoft.Search/searchServices@2023-11-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: skuName }
  properties: {
    replicaCount: replicaCount
    partitionCount: 1
    hostingMode: 'default'
    semanticSearch: 'standard' // semantic ranker for reranking (FR-B1)
    disableLocalAuth: true // Entra ID only; no admin/query keys
    publicNetworkAccess: 'disabled'
  }
}

resource roles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for r in roleAssignments: {
  scope: search
  name: guid(search.id, r.principalId, r.roleDefinitionId)
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
    targetResourceId: search.id
    groupId: 'searchService'
    subnetId: privateEndpointSubnetId
    privateDnsZoneIds: privateDnsZoneIds
  }
}

output id string = search.id
output endpoint string = 'https://${search.name}.search.windows.net'
