using Microsoft.AspNetCore.OutputCaching;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Wire up Redis as the output cache backing store.
// "cache" must match the name used in AddRedis("cache") in the AppHost.
builder.AddRedisOutputCache("cache");

var app = builder.Build();

app.UseOutputCache();

// Register /health and /alive endpoints required by Aspire
app.MapDefaultEndpoints();

// Products endpoint – returns a static list for now (Lab 4 adds the database)
app.MapGet("/products", [OutputCache(Duration = 60)] () =>
{
    var products = new[]
    {
        new Product(1, "Cloud T-Shirt", 19.99m, "Apparel"),
        new Product(2, "Aspire Mug", 14.99m, "Accessories"),
        new Product(3, "Docker Sticker", 2.99m, "Accessories"),
        new Product(4, "Kubernetes Hoodie", 49.99m, "Apparel"),
        new Product(5, "NuGet Notebook", 9.99m, "Stationery"),
    };
    return Results.Ok(products);
});

app.MapGet("/products/count", () => new { Count = 42, UpdatedAt = DateTime.UtcNow });

app.Run();

// Minimal record used as the API response shape
record Product(int Id, string Name, decimal Price, string Category);