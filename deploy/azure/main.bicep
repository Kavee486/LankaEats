// LankaEats on Azure Container Apps, sized for the free monthly allowance or Azure for Students credit.
//
// Stateful dependencies are free-tier SaaS, not Azure resources (the Azure equivalents cost money):
//   PostgreSQL -> Neon or Supabase free tier (one project, three databases)
//   RabbitMQ   -> CloudAMQP "Little Lemur" free plan
//   Redis      -> Upstash free tier
//   Traces     -> optional: Grafana Cloud free tier (OTLP)
//
// Every app scales to zero. HTTP traffic wakes the gateway and APIs, and KEDA RabbitMQ rules wake
// the consumers when their queue has messages.

targetScope = 'resourceGroup'

@description('Azure region.')
param location string = resourceGroup().location

@description('Container registry prefix, e.g. ghcr.io/your-github-user (GHCR packages must be public).')
param registry string

@description('Image tag to deploy (the CI pushes the commit SHA and latest).')
param imageTag string = 'latest'

@description('Minimum replicas per app. 0 keeps the cost near zero; 1 avoids cold starts during a demo.')
@minValue(0)
param minReplicas int = 0

@secure()
@description('Npgsql connection string for the orders database (Neon: ...;SSL Mode=Require).')
param ordersDb string

@secure()
param inventoryDb string

@secure()
param paymentsDb string

@secure()
@description('AMQP URL, e.g. amqps://user:pass@host/vhost from CloudAMQP.')
param rabbitmqUrl string

@secure()
@description('StackExchange.Redis connection string, e.g. host:6379,password=...,ssl=True,abortConnect=False (Upstash).')
param redisConnection string

@secure()
@description('At least 32 random characters. Generate it with: openssl rand -base64 48')
param jwtSigningKey string

@description('Optional OTLP/HTTP endpoint for traces, logs and metrics (e.g. Grafana Cloud). Leave empty to disable.')
param otlpEndpoint string = ''

@description('Optional OTLP headers, e.g. Authorization=Basic xxx')
param otlpHeaders string = ''

var prefix = 'lankaeats'
var appName = {
  orders: '${prefix}-orders'
  inventory: '${prefix}-inventory'
  payments: '${prefix}-payments'
  notifications: '${prefix}-notifications'
  gateway: '${prefix}-gateway'
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    // ~4.5 GB/month stays inside the 5 GB/month free Log Analytics ingestion.
    workspaceCapping: { dailyQuotaGb: json('0.15') }
  }
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var commonSecrets = [
  { name: 'rabbitmq', value: rabbitmqUrl }
  { name: 'redis', value: redisConnection }
  { name: 'jwt-signing-key', value: jwtSigningKey }
]

var otelEnv = empty(otlpEndpoint) ? [] : [
  { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
  { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: 'http/protobuf' }
  { name: 'OTEL_EXPORTER_OTLP_HEADERS', value: otlpHeaders }
]

var commonEnv = concat([
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'ConnectionStrings__rabbitmq', secretRef: 'rabbitmq' }
  { name: 'ConnectionStrings__redis', secretRef: 'redis' }
  { name: 'Jwt__SigningKey', secretRef: 'jwt-signing-key' }
], otelEnv)

var apps = [
  {
    key: 'orders'
    external: false
    queues: ['orders-stock-reserved', 'orders-stock-reservation-failed', 'orders-payment-succeeded', 'orders-payment-failed']
    secrets: [{ name: 'db', value: ordersDb }]
    env: [
      { name: 'ConnectionStrings__orders', secretRef: 'db' }
      { name: 'Services__Inventory', value: 'http://${appName.inventory}' }
    ]
  }
  {
    key: 'inventory'
    external: false
    queues: ['inventory-order-submitted', 'inventory-payment-failed', 'inventory-payment-succeeded']
    secrets: [{ name: 'db', value: inventoryDb }]
    env: [{ name: 'ConnectionStrings__inventory', secretRef: 'db' }]
  }
  {
    key: 'payments'
    external: false
    queues: ['payments-stock-reserved']
    secrets: [{ name: 'db', value: paymentsDb }]
    env: [{ name: 'ConnectionStrings__payments', secretRef: 'db' }]
  }
  {
    key: 'notifications'
    external: false
    queues: ['notifications-order-status-changed']
    secrets: []
    env: []
  }
  {
    key: 'gateway'
    external: true
    queues: []
    secrets: []
    env: [
      { name: 'Auth__EnableDemoLogin', value: 'true' }
      { name: 'ReverseProxy__Clusters__orders__Destinations__primary__Address', value: 'http://${appName.orders}' }
      { name: 'ReverseProxy__Clusters__inventory__Destinations__primary__Address', value: 'http://${appName.inventory}' }
      { name: 'ReverseProxy__Clusters__payments__Destinations__primary__Address', value: 'http://${appName.payments}' }
      { name: 'ReverseProxy__Clusters__notifications__Destinations__primary__Address', value: 'http://${appName.notifications}' }
    ]
  }
]

resource containerApps 'Microsoft.App/containerApps@2024-03-01' = [for app in apps: {
  name: appName[app.key]
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: concat(commonSecrets, app.secrets)
      ingress: {
        external: app.external
        targetPort: 8080
        transport: 'auto' // supports WebSockets for SignalR
        // Internal service-to-service calls use plain HTTP inside the environment. Public traffic is HTTPS only.
        allowInsecure: !app.external
      }
    }
    template: {
      containers: [
        {
          name: app.key
          image: '${registry}/lankaeats-${app.key}:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(commonEnv, [{ name: 'OTEL_SERVICE_NAME', value: app.key }], app.env)
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 15, initialDelaySeconds: 5 }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: 3
        rules: concat(
          [{ name: 'http', http: { metadata: { concurrentRequests: '50' } } }],
          map(app.queues, queue => {
            name: queue
            custom: {
              type: 'rabbitmq'
              metadata: { queueName: queue, mode: 'QueueLength', value: '5' }
              auth: [{ secretRef: 'rabbitmq', triggerParameter: 'host' }]
            }
          }))
      }
    }
  }
}]

output gatewayUrl string = 'https://${containerApps[4].properties.configuration.ingress.fqdn}'
