// Wabi Insight on Azure: one Linux App Service runs the API and serves the web app (Web:RootPath), Azure SQL Database
// holds the configuration database, and a private Blob Storage container holds the generated and uploaded files.
// Deployed by .github/workflows/deploy-azure.yml (option "Azure resources"); guide: docs/DEPLOYMENT_AZURE.md.
// Idempotent: running it again updates the resources to this template (app settings included).
targetScope = 'resourceGroup'

@description('Azure region for every resource (for example uaenorth).')
param location string = resourceGroup().location

@description('Web app name. Its address is https://<appName>.azurewebsites.net. Globally unique; lowercase letters, digits and hyphens.')
@minLength(2)
@maxLength(50)
param appName string

@description('App Service plan name.')
param planName string = '${appName}-plan'

@description('App Service plan size. B1 or larger: Always On (needed by the background jobs) is not available on Free/Shared.')
@allowed([
  'B1'
  'B2'
  'B3'
  'S1'
  'S2'
  'P0v3'
  'P1v3'
  'P2v3'
])
param planSku string = 'B1'

@description('Azure SQL logical server name (globally unique).')
param sqlServerName string = '${appName}-sql'

@description('Configuration database name.')
param databaseName string = 'WabiInsightDB'

@description('Database size (DTU tiers): Basic (2 GB), S0, S1, S2 (250 GB).')
@allowed([
  'Basic'
  'S0'
  'S1'
  'S2'
])
param databaseSku string = 'S0'

@description('SQL administrator login (also used by the application and by the migrations).')
param sqlAdminLogin string = 'wabiadmin'

@description('SQL administrator password (GitHub environment secret AZURE_SQL_ADMIN_PASSWORD).')
@secure()
param sqlAdminPassword string

@description('Storage account name: 3-24 lowercase letters and digits, globally unique.')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Private container for reports, exports, attachments and logos.')
param containerName string = 'wabi-insight-files'

@description('Address users open, used in e-mailed links (the custom domain when there is one).')
param publicUrl string = 'https://${appName}.azurewebsites.net'

var databaseTier = databaseSku == 'Basic' ? 'Basic' : 'Standard'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: planSku
  }
  properties: {
    reserved: true
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    // Deleted files and containers can be recovered for 14 days.
    deleteRetentionPolicy: {
      enabled: true
      days: 14
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 14
    }
  }
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    version: '12.0'
  }
}

// Lets Azure services (the App Service) reach the server; the deployment workflow opens the runner's own address only
// for the length of a migration and removes it again.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: databaseSku
    tier: databaseTier
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${databaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet WabiInsight.Api.dll'
      alwaysOn: true
      healthCheckPath: '/health/live'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          // Environment variables override the connection string committed in appsettings.json (owner decision G).
          name: 'ConnectionStrings__ConfigurationDatabase'
          value: sqlConnectionString
        }
        {
          // /home is the App Service's persistent, shared storage: the key ring survives restarts and deployments.
          name: 'DataProtection__KeyRingPath'
          value: '/home/data/wabi-insight/keys'
        }
        {
          name: 'Storage__Provider'
          value: 'AzureBlob'
        }
        {
          name: 'Storage__RootPath'
          value: '/home/data/wabi-insight/storage'
        }
        {
          name: 'Storage__AzureBlob__ConnectionString'
          value: storageConnectionString
        }
        {
          name: 'Storage__AzureBlob__ContainerName'
          value: containerName
        }
        {
          // The API serves the Angular build from here; the workflow's "Web app" option deploys it.
          name: 'Web__RootPath'
          value: '/home/site/web'
        }
        {
          name: 'Platform__BaseUrl'
          value: publicUrl
        }
        {
          // The App Service front end is the only way in; it appends the real caller to X-Forwarded-For.
          name: 'ReverseProxy__TrustAllProxies'
          value: 'true'
        }
        {
          name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
          value: 'true'
        }
      ]
    }
  }
  dependsOn: [
    database
    container
    allowAzureServices
  ]
}

output appUrl string = 'https://${app.properties.defaultHostName}'
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output storageAccount string = storage.name
