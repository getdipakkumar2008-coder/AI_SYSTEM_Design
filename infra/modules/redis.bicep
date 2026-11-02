param name string
param location string
param tags object
param privateEndpointSubnetId string
param privateDnsZoneIds array
@description('Azure Managed Redis SKU, for example Balanced_B0 (dev) or Balanced_B5 (prod).')
param skuName string = 'Balanced_B0'
@description('Principals granted data access through Entra ID (no access keys).')
param dataPrincipalIds array = []

// Azure Managed Redis uses the Microsoft.Cache/redisEnterprise resource type.
resource cluster 'Microsoft.Cache/redisEnterprise@2025-07-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: skuName }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
  }
}

resource database 'Microsoft.Cache/redisEnterprise/databases@2025-07-01' = {
  parent: cluster
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    port: 10000
    clusteringPolicy: 'EnterpriseCluster' // RediSearch vector search requires the enterprise policy
    evictionPolicy: 'NoEviction'
    accessKeysAuthentication: 'Disabled'
    modules: [
      { name: 'RediSearch' }
    ]
  }
}

resource access 'Microsoft.Cache/redisEnterprise/databases/accessPolicyAssignments@2025-07-01' = [for (principalId, i) in dataPrincipalIds: {
  parent: database
  name: 'assign${i}'
  properties: {
    accessPolicyName: 'default'
    user: { objectId: principalId }
  }
}]

module pe 'privateEndpoint.bicep' = {
  name: 'pe-${name}'
  params: {
    name: 'pe-${name}'
    location: location
    tags: tags
    targetResourceId: cluster.id
    groupId: 'redisEnterprise'
    subnetId: privateEndpointSubnetId
    privateDnsZoneIds: privateDnsZoneIds
  }
  dependsOn: [database]
}

output id string = cluster.id
output hostName string = cluster.properties.hostName
