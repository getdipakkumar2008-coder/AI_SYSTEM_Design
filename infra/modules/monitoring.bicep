@description('Base name for monitoring resources.')
param name string
param location string
param tags object
@description('Log retention in days.')
param retentionInDays int = 30

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${name}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: retentionInDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${name}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    // Use Entra ID auth for telemetry ingestion; no instrumentation-key-only ingestion.
    DisableLocalAuth: true
  }
}

output workspaceId string = workspace.id
output workspaceName string = workspace.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsId string = appInsights.id
