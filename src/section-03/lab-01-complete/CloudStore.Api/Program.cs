using CloudStore.Infrastructure.Data;
using CloudStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Azure.Storage.Blobs;
using Azure.Data.Tables;
using Azure.Storage.Blobs.Models;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        var frontEndUri = Environment.GetEnvironmentVariable("Angular_FrontEnd") ?? "http://localhost:4200";
        policy.WithOrigins(frontEndUri)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Database
builder.AddAzureNpgsqlDbContext<CloudStoreDbContext>(connectionName: "PostgreSQL");

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
builder.AddAzureBlobServiceClient("blobs");
builder.AddAzureTableServiceClient("tables");

// Services
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IStorageService, StorageService>();

var app = builder.Build();

app.MapDefaultEndpoints();

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
