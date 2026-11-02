param name string
param location string
param tags object
param tenantId string
param privateEndpointSubnetId string
param privateDnsZoneIds array
@description('Role assignments: [{ principalId, roleDefinitionId, principalType }]')
param roleAssignments array = []

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled'
    networkAcls: { defaultAction: 'Deny', bypass: 'AzureServices' }
  }
}

resource roles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for r in roleAssignments: {
  scope: vault
  name: guid(vault.id, r.principalId, r.roleDefinitionId)
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
    targetResourceId: vault.id
    groupId: 'vault'
    subnetId: privateEndpointSubnetId
    privateDnsZoneIds: privateDnsZoneIds
  }
}

output id string = vault.id
output uri string = vault.properties.vaultUri
