param name string
param location string
param tags object
param addressSpace string = '10.10.0.0/16'
param privateEndpointSubnetPrefix string = '10.10.1.0/24'
param appSubnetPrefix string = '10.10.2.0/23'

// Private DNS zones for the Private Link services used by this workload.
var privateDnsZones = {
  vault: 'privatelink.vaultcore.azure.net'
  cosmos: 'privatelink.documents.azure.com'
  search: 'privatelink.search.windows.net'
  openai: 'privatelink.openai.azure.com'
  cognitive: 'privatelink.cognitiveservices.azure.com'
  servicebus: 'privatelink.servicebus.windows.net'
  redis: 'privatelink.redis.azure.net'
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: 'vnet-${name}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: [addressSpace] }
    subnets: [
      {
        name: 'snet-private-endpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          privateEndpointNetworkPolicies: 'Enabled'
        }
      }
      {
        name: 'snet-apps'
        properties: {
          addressPrefix: appSubnetPrefix
          delegations: [
            {
              name: 'container-apps'
              properties: { serviceName: 'Microsoft.App/environments' }
            }
          ]
        }
      }
    ]
    // TODO: attach NSGs to both subnets and restrict egress.
  }
}

resource zones 'Microsoft.Network/privateDnsZones@2020-06-01' = [for zone in items(privateDnsZones): {
  name: zone.value
  location: 'global'
  tags: tags
}]

resource zoneLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = [for (zone, i) in items(privateDnsZones): {
  parent: zones[i]
  name: 'link-${name}'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnet.id }
  }
}]

output vnetId string = vnet.id
output privateEndpointSubnetId string = '${vnet.id}/subnets/snet-private-endpoints'
output appSubnetId string = '${vnet.id}/subnets/snet-apps'
output zoneNames object = privateDnsZones
