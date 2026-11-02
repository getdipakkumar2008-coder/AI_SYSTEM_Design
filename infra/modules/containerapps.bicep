param name string
param location string
param tags object
param infrastructureSubnetId string
param logAnalyticsWorkspaceName string
param userAssignedIdentityId string
@description('Container image for the API. The default is a placeholder; CI replaces it.')
param image string = 'mcr.microsoft.com/k8se/quickstart:latest'
param minReplicas int = 1
param maxReplicas int = 10
@description('Concurrent HTTP requests per replica that trigger scale-out (capacity model: size from concurrency).')
param concurrentRequestsPerReplica int = 20
@description('Non-secret app settings. Secrets belong in Key Vault, referenced by identity.')
param environmentVariables array = []

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${name}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: infrastructureSubnetId
      internal: true // reachable only from inside the VNet (Front Door / APIM)
    }
    workloadProfiles: [
      { name: 'Consumption', workloadProfileType: 'Consumption' }
    ]
    zoneRedundant: false // TODO(prod): true (needs a /23 subnet, already provisioned)
  }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-${name}-api'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${userAssignedIdentityId}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Multiple' // enables canary traffic splitting (FR-D5)
      ingress: {
        external: true // external within the internal environment, i.e. VNet-visible
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: environmentVariables
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 } }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-concurrency'
            http: { metadata: { concurrentRequests: string(concurrentRequestsPerReplica) } }
          }
        ]
      }
    }
  }
}

output environmentId string = environment.id
output apiFqdn string = api.properties.configuration.ingress.fqdn
