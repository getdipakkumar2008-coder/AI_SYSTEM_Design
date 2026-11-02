param name string
param location string
param tags object
param privateEndpointSubnetId string
param privateDnsZoneIds array
@description('Principals granted the Cosmos DB built-in data contributor role (data plane).')
param dataContributorPrincipalIds array = []

var databaseName = 'assistant'
// Cosmos DB Built-in Data Contributor (data-plane role, not an ARM role).
var dataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: name
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      { locationName: location, failoverPriority: 0, isZoneRedundant: false }
    ]
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    disableLocalAuth: true // Entra ID only; no keys
    publicNetworkAccess: 'Disabled'
    // TODO(prod): zone redundancy, a second region, and continuous backup (NFR-7).
    backupPolicy: { type: 'Continuous', continuousModeProperties: { tier: 'Continuous7Days' } }
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: account
  name: databaseName
  properties: {
    resource: { id: databaseName }
  }
}

// Per-user conversation state. Partition key = user id keeps one user's data in one logical partition.
resource sessions 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'sessions'
  properties: {
    resource: {
      id: 'sessions'
      partitionKey: { paths: ['/userId'], kind: 'Hash' }
      defaultTtl: -1 // enables per-item ttl (FR-C4)
    }
  }
}

// Single-use write confirmation tokens (FR-B4). Short default TTL; updates use ETag preconditions.
resource pendingActions 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'pendingActions'
  properties: {
    resource: {
      id: 'pendingActions'
      partitionKey: { paths: ['/userId'], kind: 'Hash' }
      defaultTtl: 900
    }
  }
}

resource dataRoles 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = [for principalId in dataContributorPrincipalIds: {
  parent: account
  name: guid(account.id, principalId, dataContributorRoleId)
  properties: {
    principalId: principalId
    roleDefinitionId: '${account.id}/sqlRoleDefinitions/${dataContributorRoleId}'
    scope: account.id
  }
}]

module pe 'privateEndpoint.bicep' = {
  name: 'pe-${name}'
  params: {
    name: 'pe-${name}'
    location: location
    tags: tags
    targetResourceId: account.id
    groupId: 'Sql'
    subnetId: privateEndpointSubnetId
    privateDnsZoneIds: privateDnsZoneIds
  }
}

output id string = account.id
output endpoint string = account.properties.documentEndpoint
output databaseName string = databaseName
