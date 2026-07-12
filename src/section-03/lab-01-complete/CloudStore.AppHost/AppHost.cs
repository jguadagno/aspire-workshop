var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("postgres-data")
    .WithPgAdmin();
var cloudStoreDb = postgres.AddDatabase("PostgreSQL", "cloudstore");

var redis = builder.AddRedis("redis");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite =>
    {
        azurite.WithDataVolume("storage-data");
    });
var blobs = storage.AddBlobs("blobs");
var tables = storage.AddTables("tables");

var api = builder.AddProject<Projects.CloudStore_Api>("api")
    .WithReference(cloudStoreDb)
    .WithReference(redis)
    .WithReference(blobs)
    .WithReference(tables)
    .WaitFor(cloudStoreDb)
    .WaitFor(redis)
    .WaitFor(blobs)
    .WaitFor(tables);

var web = builder.AddViteApp("web", "../CloudStore.Web", "start")
    .WithReference(api)
    .WithEnvironment("VITE_API_BASE_URL", api.GetEndpoint("http"))
    .WaitFor(api);

api.WithEnvironment("Angular_FrontEnd", web.GetEndpoint("http"));

builder.Build().Run();