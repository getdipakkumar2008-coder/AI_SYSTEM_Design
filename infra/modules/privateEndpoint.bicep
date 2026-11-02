@description('Name of the private endpoint.')
param name string
param location string
param tags object
@description('Resource id of the service to expose privately.')
param targetResourceId string
@description('Private Link sub-resource, for example vault, Sql, searchService, account, namespace, redisEnterprise.')
param groupId string
param subnetId string
@description('Private DNS zone ids to register the endpoint in.')
param privateDnsZoneIds array

resource endpoint 'Microsoft.Network/privateEndpoints@2024-01-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    subnet: { id: subnetId }
    privateLinkServiceConnections: [
      {
        name: name
        properties: {
          privateLinkServiceId: targetResourceId
          groupIds: [groupId]
        }
      }
    ]
  }
}

resource dnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-01-01' = {
  parent: endpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [for (zoneId, i) in privateDnsZoneIds: {
      name: 'zone${i}'
      properties: { privateDnsZoneId: zoneId }
    }]
  }
}
