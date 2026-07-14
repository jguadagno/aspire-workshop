using CloudStore.Infrastructure.Data;
using CloudStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Azure.Storage.Blobs;
using Azure.Data.Tables;
using Azure.Storage.Blobs.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Database
var pgConnection = builder.Configuration.GetConnectionString("PostgreSQL");
builder.Services.AddDbContext<CloudStoreDbContext>(options =>
    options.UseNpgsql(pgConnection)
);

// Redis (optional, graceful fallback if not available)
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConnection))
{
    try
    {
        var redis = ConnectionMultiplexer.Connect(redisConnection);
        builder.Services.AddSingleton(redis);
    }
    catch
    {
        // Redis not available, will skip caching
    }
}

// Azure Storage (Azurite emulator)
var azureStorageConnection = builder.Configuration.GetConnectionString("AzureStorage");

var blobContainer = new BlobContainerClient(azureStorageConnection, "product-images");
// Create container if it doesn't exist
try
{
    await blobContainer.CreateIfNotExistsAsync();
    await blobContainer.SetAccessPolicyAsync(PublicAccessType.Blob);
}
catch { /* Container creation might fail in some scenarios, will be handled during upload */ }

builder.Services.AddSingleton(blobContainer);

// Azure Table Storage (Azurite emulator)
var tableClient = new TableClient(azureStorageConnection, "ProductImageQueue");
// Create table if it doesn't exist
try
{
    await tableClient.CreateIfNotExistsAsync();
}
catch { /* Table creation might fail in some scenarios, will be handled during insert */ }

builder.Services.AddSingleton(tableClient);

// Services
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IStorageService, StorageService>();
builder.Services.AddScoped<IImageService, ImageService>();

var app = builder.Build();

// Apply migrations on startup
if (builder.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CloudStoreDbContext>();
    try
    {
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Could not apply migrations: {ExMessage}", ex.Message);
    }
}

app.UseCors("AllowAngular");
app.MapControllers();

app.Run();
