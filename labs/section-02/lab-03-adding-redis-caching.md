# Lab 3 – Adding Redis Caching

**Estimated time:** 20 minutes  
**Difficulty:** Intermediate  
**Prerequisites:** Completed [Lab 2](./lab-02-orchestrating-services-with-the-app-host.md), Docker Desktop running
**Required Software:** [Workshop Requirements](../../requirements.md)

---

## Objectives

By the end of this lab you will:

- ✅ Add a Redis container resource to the App Host
- ✅ Connect ProductsApi to Redis for output caching
- ✅ Apply output cache to a component/endpoint
- ✅ Observe cache hits and misses in the Aspire dashboard traces

---

## Background

Aspire's hosting integrations make it trivial to spin up infrastructure like Redis as a local Docker container — no `docker-compose.yml` required. The client integration packages then configure the correct connection string automatically from the service reference.

**How it works:**

```text
AppHost registers Redis container
      ↓
WithReference(cache) injects connection string env var into Web
      ↓
AddRedisOutputCache("cache") reads that env var and configures StackExchange.Redis
      ↓
Blazor pages/endpoints decorated with [OutputCache] use Redis as the backing store
```

---

> Note: To preserve the previous lab, open Explorer or Finder and copy `lab-02-orchestrating-services` to `lab-03-add-redis-cache` before running the following commands.

## Step 1 – Add the Redis Hosting Integration to AppHost

```bash
dotnet add CloudStore.AppHost package Aspire.Hosting.Redis
```

---

## Step 2 – Register Redis in the App Host

Open `CloudStore.AppHost/AppHost.cs` and add the Redis resource:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Add a Redis container (Aspire pulls the image automatically)
var cache = builder.AddRedis("cache");

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
```

> 💡 `AddRedis("cache")` pulls the official `redis` Docker image and starts a container. The name `"cache"` is used as the connection string name that the client integration looks up.
>
> ***Optional***: Add [Redis Insight](https://github.com/RedisInsight/RedisInsight), a Redis management tool from the Redis team. As part of the `Aspire.Hosting.Redis` package, Redis Insight is available in the same integration. Update the Redis resource to call the `WithRedisInsight()` method to enable the method on the returned builder:

```csharp
var cache = builder.AddRedis("cache")
    .WithRedisInsight(); // <-- Optional, adds RedisInsight for monitoring the Redis instance
```

---

## Step 3 – Add the Redis Output Cache Client Integration to the ProductsApi Project

```bash
dotnet add CloudStore.ProductsApi package Aspire.StackExchange.Redis.OutputCaching
```

---

## Step 4 – Register Redis Output Cache in ProductsApi

Open `CloudStore.ProductsApi/Program.cs` and add `AddRedisOutputCache("cache")` to register the Redis-backed output cache store just after the `builder.AddServiceDefaults();` line:

```csharp
// Wire up Redis as the output cache backing store.
// "cache" must match the name used in AddRedis("cache") in the AppHost.
builder.AddRedisOutputCache("cache");
```

Then after the line `var app = builder.Build();`, add `app.UseOutputCache();` to enable output caching in the middleware pipeline:

```csharp
app.UseOutputCache();
```

---

## Step 5 – Cache the Products Api Response

If you add a minimal API route in the Api project, you can use the `[OutputCache]` attribute directly.

Continuing in the `CloudStore.ProductsApi/Program.cs` file, update the `/products` endpoint to use the `[OutputCache]` attribute:

Change this:

```csharp
app.MapGet("/products", () =>
```

To this:

```csharp
app.MapGet("/products", [OutputCache(Duration = 60)] () =>
```

You'll need to import the using statement for the `OutputCache` attribute:

```csharp
using Microsoft.AspNetCore.OutputCaching;
```

This will cache the response for 60 seconds. You can adjust the duration as needed.

---

## Step 6 – Cache the Products Page in the Web Frontend

From the command line, add the `Aspire.StackExchange.Redis.OutputCaching` package to the Web project:

```bash
dotnet add CloudStore.Web package Aspire.StackExchange.Redis.OutputCaching
```

Now, just like the ProductsApi, we need to register the Redis output cache in the Web project. Open `CloudStore.Web/Program.cs` and add `AddRedisOutputCache("cache")` after the `builder.AddServiceDefaults();` line:

```csharp
// Wire up Redis as the output cache backing store.
// "cache" must match the name used in AddRedis("cache") in the AppHost
builder.AddRedisOutputCache("cache");
```

Now, we need to configure the output cache in the Web project. Open `CloudStore.Web/Pages/Products.razor` and change the `@attribute` line to add the `[OutputCache]` attribute to the `@page` directive:

```razor
@attribute [OutputCache(Duration = 30), StreamRendering(true)]
```

## Step 7 – Run and Verify

```bash
aspire run
```

In the dashboard you should now see a **fourth resource** — the Redis container:

| Name           | Type      | Expected state |
| -------------- | --------- | -------------- |
| `apiservice`   | Project   | Running        |
| `productsapi`  | Project   | Running        |
| `webfrontend`  | Project   | Running        |
| `cache`        | Container | Running        |
| `redisinsight` | Container | Running        |

### Verify Redis is working

1. Open the web app → navigate to **Products** (first request — cache **miss**)
2. Refresh the page quickly (within 60 s) — this should be a cache **hit**
3. In the dashboard → **Traces**, compare the two requests:
   - **Cache miss:** trace has spans going out to `productsapi`
   - **Cache hit:** trace stays within `webfrontend` only — no outbound call

> 💡 Cache hits are significantly faster. Look at the total duration column in the Traces list.

---

## Expected Outcome

- Redis container appears in the dashboard Resources view
- First request to `/products` results in a full trace across `webfrontend` → `productsapi`
- Subsequent requests within 60 seconds show a much shorter trace (cache hit, no outbound call)

---

## Troubleshooting

| Problem                                     | Solution                                                                                  |
| ------------------------------------------- | ----------------------------------------------------------------------------------------- |
| Redis container stuck in "Starting"         | Check Docker is running; run `docker ps` to confirm                                       |
| `ConnectionMultiplexer` exception in logs   | Confirm `WithReference(cache)` is set on `webfrontend` and `WaitFor(cache)` is in AppHost |
| Cache never hits (always calls productsapi) | Verify `app.UseOutputCache()` is called before mapping endpoints in `Program.cs`          |
| `AddRedisOutputCache` method not found      | Confirm the `Aspire.StackExchange.Redis.OutputCaching` package is installed               |

---

## Summary

| Concept                                  | Location                                                                 |
| ---------------------------------------- | ------------------------------------------------------------------------ |
| Add Redis container to local environment | `AppHost/Program.cs` → `AddRedis("cache")`                               |
| Connect service to Redis                 | `WithReference(cache)` on the consuming project                          |
| Register output cache with Redis         | `AddRedisOutputCache("cache")` in ProductsApi `Program.cs`               |
| Apply cache policy                       | `.CacheOutput(p => p.Expire(...))` on route or `[OutputCache]` attribute |
| Observe cache behavior                   | Dashboard → Traces (compare hit vs miss waterfall)                       |

➡️ **Next:** [Lab 4 – Adding a Microsoft SQL Server Database](./lab-04-adding-a-microsoft-sql-server-database.md)
