var builder = DistributedApplication.CreateBuilder(args);

// Add a Redis container (Aspire pulls the image automatically)
var cache = builder.AddRedis("cache")
    .WithRedisInsight();

var apiService = builder.AddProject<Projects.CloudStore_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

var productsApi = builder.AddProject<Projects.CloudStore_ProductsApi>("productsapi")
    .WithReference(cache)
    .WithHttpHealthCheck("/health")
    .WaitFor(cache);

builder.AddProject<Projects.CloudStore_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WithReference(productsApi)
    .WithReference(cache)       // <-- inject Redis connection string into web
    .WaitFor(apiService)
    .WaitFor(productsApi)
    .WaitFor(cache);            // <-- wait for Redis to be ready

builder.Build().Run();