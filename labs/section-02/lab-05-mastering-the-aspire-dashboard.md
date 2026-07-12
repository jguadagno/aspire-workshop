# Lab 5 – Mastering the Aspire Dashboard

**Estimated time:** 20 minutes  
**Difficulty:** Beginner–Intermediate  
**Prerequisites:** Completed [Lab 4](./lab-04-adding-a-microsoft-sql-server-database.md) — all services running
**Required Software:** [Workshop Requirements](../../requirements.md)

---

## Objectives

By the end of this lab you will:

- ✅ Navigate all four dashboard sections (Resources, Logs, Traces, Metrics)
- ✅ Filter structured logs by resource and severity
- ✅ Read a distributed trace waterfall across multiple services
- ✅ Find database query spans with their SQL
- ✅ Add a custom Activity span with tags
- ✅ Understand what metrics are emitted automatically

---

## Background

The Aspire Dashboard is a local, zero-config observability UI powered by OpenTelemetry. It collects:

| Signal              | Examples                                                             |
| ------------------- | -------------------------------------------------------------------- |
| **Traces**          | HTTP requests, DB queries, cache operations — linked across services |
| **Structured Logs** | Log messages with key-value pairs, correlated to traces by TraceId   |
| **Metrics**         | HTTP request counts, latency histograms, GC stats, custom meters     |

The dashboard is **only for local development** — in production, you'd ship the same OpenTelemetry signals to Azure Monitor, Jaeger, Grafana, etc.

---

## Step 1 – Navigate the Resources View

> Note: To preserve the previous lab, open Explorer or Finder and copy `lab-04-add-database` to `lab-05-dashboard-deep-dive` before running the following commands.
> Note: Since we are copying the previous lab, Aspire will treat it as a new app and assign new resource IDs. This means the database server will be recreated, but the data will still be there since it is stored in a volume. You will need to stop the previous SQL Server instance before starting this lab.

```bash
docker stop cloudstore-sqlserver-<random-id>
```

> Replace `<random-id>` with the actual ID from your previous lab, which you can find by running `docker ps -a` and looking for the container named `cloudstore-sqlserver-<random-id>`.

You can also stop it from the docker desktop UI by finding the container with the name `cloudstore-sqlserver-<random-id>` and clicking the stop button.

The container is also safe to delete if you want to free up resources.

```bash
docker rm cloudstore-sqlserver-<random-id>
```

### Start the AppHost

If not already running, start the AppHost from the `lab-05-dashboard-deep-dive` folder:

```bash
aspire run
```

1. Open the Aspire Dashboard (URL from terminal when you started the AppHost)
2. The **Resources** view is the landing page
3. Examine each column:
   - **Name**: Service name
   - **State**: Running / Starting / Exited
   - **Start Time**: When the service started
   - **Source**: The container image or local project
   - **Url**: How to reach the service (click to open in a new tab)
   - **Actions**: Start / Stop buttons for each service, Console logs, and ... for more actions

Try clicking the **Stop** button (⏹) on `productsapi`.

Observe:

- `productsapi` state changes to **Exited**
- `webfrontend` health may change (it can no longer call the products API)
- Navigate to the Products page in the web app — you should see an error

Click **Start** (▶) to bring `productsapi` back up.

### Resources Graph

1. Click the **Graph** tab in the Resources view
2. This shows a live graph of service interactions based on the traces being collected.

Green checks indicate healthy interactions. If you stop a service, you'll see red X's indicating failed calls to that service.

Stop the app.

---

## Step 2 – Add Structured Logging to ProductsApi

Open `CloudStore.ProductsApi/Program.cs` and update the `/products` route to include structured log fields:

```csharp
app.MapGet("/products", [OutputCache(Duration = 60)] async (AppDbContext db, HttpContext httpContext, ILogger<Program> logger) =>
{
    logger.LogInformation(
        "Fetching products list. RequestId: {RequestId}, RemoteIP: {RemoteIP}",
        httpContext.TraceIdentifier,
        httpContext.Connection.RemoteIpAddress);

    var products = await db.Products
        .OrderBy(p => p.Category)
        .ThenBy(p => p.Name)
        .ToListAsync();

    logger.LogInformation(
        "Returned {ProductCount} products for RequestId: {RequestId}",
        products.Count,
        httpContext.TraceIdentifier);

    return Results.Ok(products);
});
```

Also add a warning log when the database is empty, after the `.ToListAsync()` call:

```csharp
if (!products.Any())
{
    logger.LogWarning(
        "Products list is empty. Check database seeding. RequestId: {RequestId}",
        httpContext.TraceIdentifier);
}
```

Restart the app and make several requests to the Products page.

---

## Step 3 – Explore Structured Logs

1. In the dashboard → **Structured**
2. In the **Resource** dropdown, select `productsapi`
3. You should see your structured log entries with individual property columns
4. Click on a log row to expand it — notice the `RequestId`, `RemoteIP`, and `ProductCount` fields are individually searchable
5. Use the **Filter** box to search for `ProductCount` — only log entries with that field appear
6. Change the **Log level** filter to **Warning** — verify only the warning (if triggered) appears
7. Change it to **Information** to see everything again

### Correlating a log to its trace

1. Click a log row that shows `Fetching products list`
2. Look for the **TraceId** column — click it
3. The dashboard navigates directly to that specific trace

---

## Step 4 – Explore the Traces View

1. In the dashboard → **Traces**
2. You'll see a list of recent traces, each with:
   - **Timestamp**: When the trace started
   - **Name**: The root span name (e.g., `GET /products`)
   - **Spans**: Number of spans, and resources involved in the trace
   - **Duration**: Total time from start to last span
   - **Actions**: The different actions you can take on the trace (e.g., View Details)
3. Click a `GET /products` trace from `webfrontend` — this trace crosses two services

### Reading the Waterfall

The waterfall shows spans as horizontal bars on a timeline:

```text
webfrontend  ├── GET /products (HTTP client call)   ─────────────────── 47ms
webfrontend  │   └── HTTP GET 200                    ──────────────── 35ms
productsapi  │       └── GET /products                ──────────── 32ms
productsapi  │           └── SELECT * FROM Products    ───── 4ms
```

Key information in each span:

- **Name**: The name of the span
- **Resource**: Which resource/service it belongs to (color-coded)
- **Duration bar** (proportional to elapsed time)
- **Actions**: The different actions you can take on the trace (e.g., View Details)

---

## Step 5 – Find the Database Query Span

1. In a trace for `GET /products`, find the span named something like `SELECT [Products]...` — this is the database query generated by EF Core
2. Click it to expand the attributes panel
3. You'll see:
   - `db.namespace`: `CloudStore`
   - `db.query.text`: The actual SQL query EF Core generated
   - `db.system.name`: `microsoft.sql_server`
   - ...and more!

This is automatic — no code changes needed. Aspire's `AddSqlServerDbContext` wires up OpenTelemetry instrumentation.

---

## Step 6 – Add a Custom Activity Span

Custom spans let you track business-logic operations in the trace, not just infrastructure calls.

Open `CloudStore/CloudStore.ProductsApi/Program.cs` and add:

```csharp
using System.Diagnostics;
```

Then after the `builder.AddServiceDefaults()` call, add:

```csharp
// Add a static ActivitySource — good practice: one per library/service
var activitySource = new ActivitySource("CloudStore.ProductsApi");

// Register it so OpenTelemetry picks it up
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("CloudStore.ProductsApi"));
```

Update the `/products` route to use the custom span:

```csharp
app.MapGet("/products", [OutputCache(Duration = 60)] async (AppDbContext db, HttpContext httpContext, ILogger<Program> logger) =>
{
    // Start a custom span wrapping the business logic
    using var activity = activitySource.StartActivity("LoadProductCatalog");

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
```

Restart the application.

- Navigate to the `/products` page in the web app to generate a trace with the new custom span.
- Go back to the dashboard
- Click on the trace for `webfrontend: Get /products` you will see in the Traces view, the waterfall now shows a `LoadProductCatalog` span
- Click the `LoadProductCatalog` span to verify `product.categories` and `product.count` appear in the attributes pane.

---

## Step 7 – Explore the Metrics View

1. In the dashboard → **Metrics**
2. Select `webfrontend` from the resource dropdown
3. Browse available metric instruments:
   - Branch: `Microsoft.AspNetCore.Hosting` -> `http.server.request.duration` – histogram of response times
   - Branch: `System.Net.Http` -> `http.client.request.duration` – outbound HTTP call times
   - Branch: `System.Runtime` -> `dotnet.gc.collections` – garbage collection counts

4. Select `http.server.request.duration` and change the **Display** to **Graph**
5. Make several requests to the Products page — the histogram should update

### Custom Meter (optional)

Add a custom metric to count product catalog loads:

Now in the `CloudStore.ProductsApi\Program.cs` file, add:

```csharp
using System.Diagnostics.Metrics;
```

Then after the `var activitySource = new ActivitySource("CloudStore.ProductsApi");` line, add:

```csharp
var meter = new Meter("CloudStore.ProductsApi");
var catalogLoadCounter = meter.CreateCounter<int>(
    "cloudstore.products.catalog_loads",
    description: "Number of times the product catalog was loaded");
```

Then update the `AddOpenTelemetry()` call to include the meter:

```csharp
// Register with OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("CloudStore.ProductsApi"))
    .WithMetrics(metrics => metrics.AddMeter("CloudStore.ProductsApi"));
```

Then in the `/products` route handler, after the `using var activity = activitySource.StartActivity("LoadProductCatalog");` line, add:

```csharp
catalogLoadCounter.Add(1, new KeyValuePair<string, object?>("cached", false));
```

After restarting, view the `Products` page in the web app.  

- Navigate to the Aspire dashboard
- Click the **Metrics** tab
- Select `productsapi` from the resource dropdown
- Find branch `CloudStore.ProductsApi` and the metric `cloudstore.products.catalog_loads` in the Metrics view.
- Click it to see the graph of how many times the product catalog was loaded. Since this is a counter, and cached, it should only increment on cache misses, every 60 seconds.

## Step 8 – Add Friendly URLs

This step is optional, but it makes the dashboard easier to use by changing endpoint labels to friendly URLs. Copy the file `lab-05-files\UrlHelper.cs` into the `CloudStore.AppHost` project.

Then in the `CloudStore.AppHost\AppHost.cs` file, we can add a new method to generate friendly URLs for the dashboard:

First the *apiservice* for the service URLs:

```csharp
var apiService = builder.AddProject<Projects.CloudStore_ApiService>("apiservice")
    .WithFriendlyUrls("CloudStore API")
    .WithHttpHealthCheck("/health");
```

Then the *productsapi* for the products API service, add this line before the `.WithReference(cache)` line:

```csharp
.WithFriendlyUrls("Products API")
```

Then the *webfrontend* for the web frontend service, add this line before the `.WithExternalHttpEndpoints()` line:

```csharp
.WithFriendlyUrls("CloudStore Web")
```

Run the app and navigate to the dashboard. You should see the friendly names in the Resources view URL column.

---

## Expected Outcome

- You can navigate all dashboard views (Resources, Structured Logs, Traces, Metrics) confidently.
- You can trace one `/products` request across `webfrontend` and `productsapi`.
- You can identify the SQL query span and inspect attributes such as `db.query.text`.
- You can see the custom `LoadProductCatalog` span and optional custom meter in trace/metric views.

---

## Troubleshooting

| Problem | Solution |
| --- | --- |
| No logs appear in **Structured** for `productsapi` | Make sure you rebuilt/restarted after adding logging code and generated requests from the Products page. |
| Trace does not cross services | Confirm `webfrontend` calls `productsapi` through the AppHost-managed endpoint and both services are running. |
| SQL span is missing in traces | Ensure the request path hits the database (cache may be serving response). Wait for cache expiration (60s) and retry. |
| Custom span `LoadProductCatalog` does not appear | Verify `ActivitySource` registration matches the source name in `AddOpenTelemetry().WithTracing(...)`. |
| Custom metric not visible | Confirm `.WithMetrics(metrics => metrics.AddMeter("CloudStore.ProductsApi"))` is configured and requests were generated after restart. |

---

## Summary

| Dashboard Section | What you learned |
| --- | --- |
| **Resources** | Service health, start/stop controls, endpoint links |
| **Structured Logs** | Filtering by resource/level, searching properties, trace correlation |
| **Traces** | Waterfall view, cross-service spans, DB query attributes |
| **Metrics** | HTTP duration histograms, GC stats, custom meters |
| **Custom spans** | `ActivitySource.StartActivity()` + `SetTag()` |

➡️ **Next:** [Lab 6 – Custom Health Checks](./lab-06-custom-health-checks.md)
