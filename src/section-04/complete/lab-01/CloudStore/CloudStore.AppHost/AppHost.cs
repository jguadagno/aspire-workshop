var builder = DistributedApplication.CreateBuilder(args);

// --- Deployment target -----------------------------------------------------------
// AddAzureContainerAppEnvironment registers the ACA compute environment.
// All project resources attach to it automatically when you run `aspire deploy`.
builder.AddAzureContainerAppEnvironment("env");

// --- Infrastructure resources ----------------------------------------------------
// AddAzureRedis:   local dev → Redis Docker container  |  deploy → Azure Cache for Redis
// RunAsContainer:  keeps local dev behavior identical to before
var cache = builder.AddAzureManagedRedis("cache")
    .RunAsContainer(c => c
        .WithClearCommand()
        .WithRedisInsight());

// AddAzureSqlServer:  local dev → SQL Server Docker container  |  deploy → Azure SQL Server
// RunAsContainer:     keeps local dev behavior identical to before
// PublishAsConnectionString() is removed — Azure SQL is provisioned via Bicep, not a plain conn string
var sql = builder.AddAzureSqlServer("cloudstore-sqlserver")
    .RunAsContainer(c => c
        .WithImageTag("2022-latest")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume("cloudstore-data")
        .WithDbGate());

var cloudStoreDb = sql.AddDatabase("CloudStore");

// --- Application services --------------------------------------------------------
var apiService = builder.AddProject<Projects.CloudStore_ApiService>("apiservice")
    .WithFriendlyUrls("CloudStore API")
    .WithHttpHealthCheck("/health");

var productsApi = builder.AddProject<Projects.CloudStore_ProductsApi>("productsapi")
    .WithFriendlyUrls("Products API")
    .WithReference(cache)
    .WithReference(cloudStoreDb)
    .WithHttpHealthCheck("/health")
    .WaitFor(cloudStoreDb)
    .WaitFor(cache);

builder.AddProject<Projects.CloudStore_Web>("webfrontend")
    .WithFriendlyUrls("CloudStore Web")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WithReference(productsApi)
    .WithReference(cache)
    .WaitFor(apiService)
    .WaitFor(productsApi)
    .WaitFor(cache);

// --- HealthChecksUI (local dev only) ---------------------------------------------
// HealthChecksUI uses the Aspire container network for health probe URLs.
// We restrict it to run mode so it does not attempt to deploy to ACA.
if (builder.ExecutionContext.IsRunMode)
{
    builder.AddHealthChecksUI("healthchecksui")
        .WithReference(apiService)
        .WithReference(productsApi)
        .WithFriendlyUrls("Health Checks UI Dashboard")
        .WithHostPort(7230);
}

builder.Build().Run();