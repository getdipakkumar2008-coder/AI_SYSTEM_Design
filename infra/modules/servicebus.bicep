param name string
param location string
param tags object
param privateEndpointSubnetId string
param privateDnsZoneIds array
@description('Role assignments: [{ principalId, roleDefinitionId, principalType }]')
param roleAssignments array = []

// Private Link on Service Bus requires the Premium tier.
resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: 'Premium', tier: 'Premium', capacity: 1 }
  properties: {
    disableLocalAuth: true
    publicNetworkAccess: 'Disabled'
    minimumTlsVersion: '1.2'
    zoneRedundant: false // TODO(prod): true
  }
}

// CDC events from commerce sources. Failed messages dead-letter after 10 deliveries (FR-C3).
resource sourceChanges 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'source-changes'
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource roles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for r in roleAssignments: {
  scope: namespace
  name: guid(namespace.id, r.principalId, r.roleDefinitionId)
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
    targetResourceId: namespace.id
    groupId: 'namespace'
    subnetId: privateEndpointSubnetId
    privateDnsZoneIds: privateDnsZoneIds
  }
}

output id string = namespace.id
output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
