using CloudStore.ProductsApi.Data;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Wire up Redis as the output cache backing store.
// "cache" must match the name used in AddRedis("cache") in the AppHost.
builder.AddRedisOutputCache("cache");

// Register AppDbContext – Aspire reads the "CloudStore" connection string
// from environment variables injected by WithReference(cloudstoreDb) in AppHost
builder.AddSqlServerDbContext<AppDbContext>("CloudStore");

var app = builder.Build();

app.UseOutputCache();

app.MapDefaultEndpoints();

// GET /products – query from the database
app.MapGet("/products", [OutputCache(Duration = 60)] async (AppDbContext db) =>
{
    var products = await db.Products
        .OrderBy(p => p.Category)
        .ThenBy(p => p.Name)
        .ToListAsync();

    return Results.Ok(products);
});

// GET /products/{id}
app.MapGet("/products/{id:int}", async (int id, AppDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    return product is null ? Results.NotFound() : Results.Ok(product);
});

app.Run();