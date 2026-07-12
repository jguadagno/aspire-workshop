var builder = DistributedApplication.CreateBuilder(args);

// Add a Redis container (Aspire pulls the image automatically)
var cache = builder.AddRedis("cache")
    .WithRedisInsight();

var sql = builder.AddSqlServer("cloudstore-sqlserver")
    .PublishAsConnectionString() // <-- publish the connection string to the dashboard and inject into consuming projects
    .WithImageTag("2022-latest") // <-- optional, defaults to latest 2022 image, if you are using a Mac, this is required since the default image is not compatible with Apple Silicon
    .WithLifetime(ContainerLifetime.Persistent) // <-- optional, defaults to transient, but we want to persist the database across restarts
    .WithDataVolume("cloudstore-data") // <-- optional, this allows you to persist the database data across restarts, otherwise it will be lost when the container is removed
    .WithDbGate();

var path = builder.AppHostDirectory;
var sqlText = string.Concat(
    " ",
    File.ReadAllText(Path.Combine(path, @"../scripts/database/create-database.sql")),
    " ",
    File.ReadAllText(Path.Combine(path, @"../scripts/database/create-tables.sql")),
    " ",
    File.ReadAllText(Path.Combine(path, @"../scripts/database/seed-data.sql")));

var cloudStoreDb = sql.AddDatabase("CloudStore")
    .WithCreationScript(sqlText);    // run SQL to create tables and seed data

var apiService = builder.AddProject<Projects.CloudStore_ApiService>("apiservice")
    .WithFriendlyUrls("CloudStore API")
    .WithHttpHealthCheck("/health");

var productsApi = builder.AddProject<Projects.CloudStore_ProductsApi>("productsapi")
    .WithFriendlyUrls("Products API")
    .WithReference(cache)
    .WithReference(cloudStoreDb)    // <-- Inject the SQL Server connection string into the products API
    .WithHttpHealthCheck("/health")
    .WaitFor(cloudStoreDb)          // <-- wait for the database to be ready before starting the products API
    .WaitFor(cache);

builder.AddProject<Projects.CloudStore_Web>("webfrontend")
    .WithFriendlyUrls("CloudStore Web")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WithReference(productsApi)
    .WithReference(cache)       // <-- inject Redis connection string into web
    .WaitFor(apiService)
    .WaitFor(productsApi)
    .WaitFor(cache);            // <-- wait for Redis to be ready

builder.Build().Run();