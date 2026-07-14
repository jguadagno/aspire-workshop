var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.CloudStore_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

// Register the new ProductsApi
var productsApi = builder.AddProject<Projects.CloudStore_ProductsApi>("productsapi")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.CloudStore_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WithReference(productsApi) // <-- inject productsapi URL into web
    .WaitFor(apiService)
    .WaitFor(productsApi); // <-- don't start web until products API is ready

builder.Build().Run();