# Lab 2 – Orchestrating Services with the App Host

**Estimated time:** 25 minutes  
**Difficulty:** Beginner–Intermediate  
**Prerequisites:** Completed [Lab 1](./lab-01-your-first-aspire-application.md)
**Required Software:** [Workshop Requirements](../../requirements.md)

---

## Objectives

By the end of this lab you will:

- ✅ Create a new minimal API project (`CloudStore.ProductsApi`)
- ✅ Register it in the App Host and connect it to the web frontend
- ✅ Use `WithReference` for automatic service discovery
- ✅ Use `WaitFor` to control startup order
- ✅ Call the new API from the Blazor frontend and see the trace

---

## Background

The App Host is a standard .NET console app that describes your distributed application as code. Adding a new service is three steps:

1. Create the project
2. Register it in the App Host
3. Reference it from consumers via `WithReference`

Aspire handles all the port assignment, environment variable injection, and service discovery automatically.

---

## Step 1 – Create the ProductsApi Project

> Note: To preserve Lab 1, open Explorer or Finder and copy `lab-01-first-aspire-app` to `lab-02-orchestrating-services` before running the following commands.

From the root `CloudStore/` directory:

> Note: Type the following command as a single line in your terminal. The line breaks are for readability.

```bash
dotnet new webapi --name CloudStore.ProductsApi
 --output CloudStore.ProductsApi
 --no-openapi
```

Single line command:

```bash
dotnet new webapi --name CloudStore.ProductsApi --output CloudStore.ProductsApi --no-openapi
```

Add it to the solution:

```bash
dotnet sln CloudStore.sln add CloudStore.ProductsApi/CloudStore.ProductsApi.csproj
```

> Note: If you copied the first lab, you will need to reopen the solution in your IDE with the new folder name (`lab-02-orchestrating-services`) to see the new project in the solution explorer.

---

## Step 2 – Add ServiceDefaults Reference

Add the shared ServiceDefaults project as a reference:

> Note: Type the following command as a single line in your terminal. The line breaks are for readability.

```bash
dotnet add CloudStore.ProductsApi/CloudStore.ProductsApi.csproj 
    reference CloudStore.ServiceDefaults/CloudStore.ServiceDefaults.csproj
```

Single line command:

```bash
dotnet add CloudStore.ProductsApi/CloudStore.ProductsApi.csproj reference CloudStore.ServiceDefaults/CloudStore.ServiceDefaults.csproj
```

---

## Step 3 – Wire Up ServiceDefaults in ProductsApi

Open `CloudStore/CloudStore.ProductsApi/Program.cs` and replace the contents:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

var app = builder.Build();

// Register /health and /alive endpoints required by Aspire
app.MapDefaultEndpoints();

// Products endpoint – returns a static list for now (Lab 4 adds the database)
app.MapGet("/products", () =>
{
    var products = new[]
    {
        new Product(1, "Cloud T-Shirt",   19.99m, "Apparel"),
        new Product(2, "Aspire Mug",      14.99m, "Accessories"),
        new Product(3, "Docker Sticker",   2.99m, "Accessories"),
        new Product(4, "Kubernetes Hoodie",49.99m, "Apparel"),
        new Product(5, "NuGet Notebook",   9.99m, "Stationery"),
    };
    return Results.Ok(products);
});

app.Run();

// Minimal record used as the API response shape
record Product(int Id, string Name, decimal Price, string Category);
```

Now move the product count endpoint you added to the Weather API in Lab 1 over to this new Products API, since it is more relevant to the product data:

- Navigate to `CloudStore/CloudStore.ApiService/Program.cs`
- Cut the following code block that defines the `products/count` endpoint:

```csharp
app.MapGet("/products/count", () => new { Count = 42, UpdatedAt = DateTime.UtcNow });
```

- Paste it into `CloudStore/CloudStore.ProductsApi/Program.cs` right below the existing `/products` endpoint:

```csharp
app.MapGet("/products/count", () => new { Count = 42, UpdatedAt = DateTime.UtcNow });
```

Typically, this `count` endpoint would query a database to return the actual number of products, but for now it just returns a static value. In Lab 4 we will connect a real database and update these endpoints to return dynamic data.

---

## Step 4 – Register ProductsApi in the App Host

Open `CloudStore/CloudStore.AppHost/AppHost.cs`. Add a reference to the AppHost `.csproj` first:

> Note: Type the following command as a single line in your terminal. The line breaks are for readability.

```bash
dotnet add CloudStore.AppHost/CloudStore.AppHost.csproj 
    reference CloudStore.ProductsApi/CloudStore.ProductsApi.csproj
```

Single line command:

```bash
dotnet add CloudStore.AppHost/CloudStore.AppHost.csproj reference CloudStore.ProductsApi/CloudStore.ProductsApi.csproj
```

Now update `AppHost.cs`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.CloudStore_ApiService>("apiservice");

// Register the new ProductsApi
var productsApi = builder.AddProject<Projects.CloudStore_ProductsApi>("productsapi");

builder.AddProject<Projects.CloudStore_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WithReference(productsApi)   // <-- inject productsapi URL into web
    .WaitFor(apiService)
    .WaitFor(productsApi);        // <-- don't start web until products API is ready

builder.Build().Run();
```

> 💡 `WithReference(productsApi)` injects `services__productsapi__http__0` (and https variant) as environment variables. The service discovery client resolves `http://productsapi` to the correct URL automatically.

---

## Step 5 – Consume ProductsApi from the Web Project

### 5a – Create a typed HTTP client

Add a new file `CloudStore/CloudStore.Web/ProductsApiClient.cs`:

```csharp
namespace CloudStore.Web;

public class ProductsApiClient(HttpClient httpClient)
{
    public async Task<Product[]> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<Product[]>(
            "/products", cancellationToken) ?? [];
    }
}

public record Product(int Id, string Name, decimal Price, string Category);
```

### 5b – Register the client in Web's Program.cs

Open `CloudStore/CloudStore.Web/Program.cs` and add the typed client registration after the existing `AddHttpClient` call of:

```csharp
builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://apiservice");
    });
```

Add a new client registration for the products API:

```csharp
// New client for the products API – Aspire resolves "productsapi" via service discovery
builder.Services.AddHttpClient<ProductsApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://productsapi");
});
```

### 5c – Add a Products Razor component

Create `CloudStore/CloudStore.Web/Components/Pages/Products.razor`:

```razor
@page "/products"
@attribute [StreamRendering(true)]
@inject ProductsApiClient ProductsApi

<PageTitle>Products</PageTitle>

<h1>CloudStore Products</h1>

@if (_products is null)
{
    <p>
        <em>Loading products...</em>
    </p>
}
else
{
    <table class="table">
        <thead>
        <tr>
            <th>ID</th>
            <th>Name</th>
            <th>Category</th>
            <th>Price</th>
        </tr>
        </thead>
        <tbody>
        @foreach (var product in _products)
        {
            <tr>
                <td>@product.Id</td>
                <td>@product.Name</td>
                <td>@product.Category</td>
                <td>@product.Price.ToString("C")</td>
            </tr>
        }
        </tbody>
    </table>
}

@code {
    private Product[]? _products;

    protected override async Task OnInitializedAsync()
    {
        _products = await ProductsApi.GetProductsAsync();
    }
}
```

### 5d – Add a nav link

In `CloudStore/CloudStore.Web/Components/Layout/NavMenu.razor`, add a menu item inside the `<nav class="nav flex-column">` block:

```razor
<div class="nav-item px-3">
    <NavLink class="nav-link" href="products">
        <span class="bi bi-box-seam-fill" aria-hidden="true"></span> Products
    </NavLink>
</div>
```

---

## Step 6 – Run and Verify

```bash
aspire run
```

In the dashboard you should now see **three** resources:

| Name          | Expected state                                 |
| ------------- | ---------------------------------------------- |
| `apiservice`  | Running                                        |
| `productsapi` | Running                                        |
| `webfrontend` | Running (started after both APIs were healthy) |

1. Open the web frontend and navigate to **Products**
2. Confirm the product table loads
3. Open the dashboard → **Traces**
4. Find a `GET /products` trace — it should show spans across **two** services (*webfrontend* → *productsapi*)

---

## Expected Outcome

- Three services visible and healthy in the Aspire dashboard
- `/products` page in the web app renders data from `ProductsApi`
- Traces show cross-service HTTP calls

---

## Troubleshooting

| Problem                                      | Solution                                                                       |
| -------------------------------------------- | ------------------------------------------------------------------------------ |
| `productsapi` not listed in dashboard        | Confirm you added the `<ProjectReference>` to AppHost and re-ran               |
| Products page shows "Loading..." forever     | Check the `productsapi` Logs in dashboard for startup errors                   |
| Service discovery fails (connection refused) | Ensure `WithReference(productsApi)` is set on `webfrontend`                    |
| `WaitFor` causes long startup                | Normal — Aspire is polling `/health`; ensure `MapDefaultEndpoints()` is called |

---

## ✨ Bonus Challenge

If you finish early, try this optional extension: add a custom health check to ProductsApi and configure AppHost to wait on it before starting the web frontend.

<https://www.c-sharpcorner.com/article/advanced-asp-net-core-health-checks/>

Add a health check that validates the products list is non-empty:

Add a reference to the health checks package:

```bash
dotnet add CloudStore.ProductsApi package Microsoft.Extensions.Diagnostics.HealthChecks
```

Open `CloudStore/CloudStore.ProductsApi/Program.cs` and add the following health check before `builder.Build()`:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("products-available", () =>
    {
        // In a real app this would query a DB; for now just return healthy
        return HealthCheckResult.Healthy("Products available");
    }, ["live"]);
```

Then add the using statement at the top of the file:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
```

Open the `CloudStore/CloudStore.AppHost/AppHost.cs` file and add `.WithHttpHealthCheck("/health");` to the *productsApi* registration to make it wait for this new health check before starting:

```csharp
// Register the new ProductsApi
var productsApi = builder.AddProject<Projects.CloudStore_ProductsApi>("productsapi")
    .WithHttpHealthCheck("/health");
```

Restart the app and verify the new health check appears in the dashboard Resources view under the `productsapi` health details.

- Click on `productsapi`, then scroll to the *Health checks* section and confirm you see `products-available` with a healthy status.

---

## Summary

| Concept                          | Location                                  |
| -------------------------------- | ----------------------------------------- |
| Registering a new project        | `AppHost/Program.cs` → `AddProject<T>`    |
| Service-to-service reference     | `WithReference(productsApi)`              |
| Startup ordering                 | `WaitFor(productsApi)`                    |
| Typed HTTP client with discovery | `AddHttpClient<T>` + `http://productsapi` |
| Default health endpoints         | `MapDefaultEndpoints()` in each service   |

➡️ **Next:** [Lab 3 – Adding Redis Caching](./lab-03-adding-redis-caching.md)
