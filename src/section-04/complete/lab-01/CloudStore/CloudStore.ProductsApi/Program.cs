using CloudStore.ProductsApi.Data;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using CloudStore.ProductsApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHealthChecks()
    .AddCheck<ProductCatalogHealthCheck>("product_catalog_health_check");

// Add a static ActivitySource — good practice: one per library/service
var activitySource = new ActivitySource("CloudStore.ProductsApi");
var meter = new Meter("CloudStore.ProductsApi");
var catalogLoadCounter = meter.CreateCounter<int>(
    "cloudstore.products.catalog_loads",
    description: "Number of times the product catalog was loaded");

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("CloudStore.ProductsApi"))
    .WithMetrics(metrics => metrics.AddMeter("CloudStore.ProductsApi"));

// Wire up Redis as the output cache backing store.
// "cache" must match the name used in AddRedis("cache") in the AppHost.
builder.AddRedisOutputCache("cache");

// Register AppDbContext – Aspire reads the "CloudStore" connection string
// from environment variables injected by WithReference(cloudstoreDb) in AppHost
builder.AddSqlServerDbContext<AppDbContext>("CloudStore", configureDbContextOptions: options =>
    AppDbContext.UseDevelopmentSeeding(options, builder.Environment));

var app = builder.Build();

// Ensure the schema exists and seed sample data, but only in Development.
// EnsureCreated() triggers the UseSeeding/UseAsyncSeeding delegates configured above.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // EnsureCreated (and its seeding) runs inside a transaction. The SQL Server
    // retrying execution strategy doesn't support user-initiated transactions,
    // so it must be invoked via CreateExecutionStrategy() to retry as a unit.
    var strategy = db.Database.CreateExecutionStrategy();
    strategy.Execute(() => db.Database.EnsureCreated());
}

app.UseOutputCache();

app.MapDefaultEndpoints();

// GET /products – query from the database
app.MapGet("/products", [OutputCache(Duration = 60)] async (AppDbContext db, HttpContext httpContext, ILogger<Program> logger) =>
{
    // Start a custom span wrapping the business logic
    using var activity = activitySource.StartActivity("LoadProductCatalog");
    catalogLoadCounter.Add(1, new KeyValuePair<string, object?>("cached", false));
    
    logger.LogInformation(
        "Fetching products list. RequestId: {RequestId}",
        httpContext.TraceIdentifier);

    var products = await db.Products
        .OrderBy(p => p.Category)
        .ThenBy(p => p.Name)
        .ToListAsync();

    // Tag the span with business-relevant data
    activity?.SetTag("product.count", products.Count);
    activity?.SetTag("product.categories", string.Join(",",
        products.Select(p => p.Category).Distinct()));

    logger.LogInformation(
        "Returned {ProductCount} products for RequestId: {RequestId}",
        products.Count,
        httpContext.TraceIdentifier);

    return Results.Ok(products);
});

// GET /products/{id}
app.MapGet("/products/{id:int}", async (int id, AppDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    return product is null ? Results.NotFound() : Results.Ok(product);
});

app.Run();