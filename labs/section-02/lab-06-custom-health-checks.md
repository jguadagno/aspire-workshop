# Lab 6 – Custom Health Checks

**Estimated time:** 20 minutes  
**Difficulty:** Beginner–Intermediate  
**Prerequisites:** Completed [Lab 5](./lab-05-mastering-the-aspire-dashboard.md) — all services running
**Required Software:** [Workshop Requirements](../../requirements.md)

---

## Objectives

By the end of this lab you will:

- ✅ Implement a custom `IHealthCheck` (`ProductCatalogHealthCheck`) in `productsapi`
- ✅ Register custom health checks in DI so they appear in service health endpoints
- ✅ Add and wire a `HealthChecksUI` resource in the AppHost with references to your services
- ✅ Configure `CloudStore.ServiceDefaults` so services expose HealthChecks UI responses securely
- ✅ Validate healthy/unhealthy states in both Aspire Dashboard and HealthChecks UI

---

## Background

In the previous labs, each service exposed a basic `/health` endpoint and Aspire used that to show whether a resource was up. That gives a quick liveness signal, but it does not always explain *why* a service is unhealthy or whether key dependencies are failing.

In this lab, you extend that model in two layers:

1. **Application health logic** in `productsapi` via a custom `IHealthCheck` (`ProductCatalogHealthCheck`) that validates a real business condition (the product catalog contains data).
2. **Cross-service health visualization** through a `HealthChecksUI` resource in the AppHost, so you can inspect detailed check results for multiple services in one place.

The Aspire Dashboard and HealthChecks UI complement each other:

| Surface              | Best for                                                                                         |
| -------------------- | ------------------------------------------------------------------------------------------------ |
| **Aspire Dashboard** | Resource state, topology, and OpenTelemetry signals (traces, logs, metrics) across the whole app |
| **HealthChecks UI**  | Detailed pass/fail results from ASP.NET Core health checks, including dependency-specific checks |

OpenTelemetry still provides the broader observability story (request flow, logs, and performance metrics), while health checks provide fast readiness/liveness and dependency status signals. In production, the same telemetry patterns can be exported to platforms like Azure Monitor, Jaeger, or Grafana.

---

## Step 1 – IHealthCheck Implementation

A custom health check is just a class that implements `IHealthCheck` and is registered in the DI container. In Lab 2, you added a simple HTTP health check to each service with `.WithHttpHealthCheck("/health")`. Now add a custom health check to the `productsapi` service that verifies the product catalog is not empty.

In the `CloudStore.ProductsApi` project, create a new file named `ProductCatalogHealthCheck.cs` with the following content:

> Note: This code can be copied from `src/section-02/lab-06-files/ProductCatalogHealthCheck.cs` if you prefer.

```csharp
using CloudStore.ProductsApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CloudStore.ProductsApi;

public class ProductCatalogHealthCheck(AppDbContext appDbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var productsExists = await appDbContext.Products.AnyAsync(cancellationToken);
        return productsExists
            ? HealthCheckResult.Healthy("Product catalog is populated.")
            : HealthCheckResult.Unhealthy("Product catalog is empty.");
    }
}
```

This health check queries the `Products` table in our database and returns *Unhealthy* if there are no products, and *Healthy* if there is at least one product.

Next, we need to register this health check in the DI container. Open `CloudStore.ProductsApi\Program.cs` and add the following line to the service configuration, after the `builder.AddServiceDefaults()` call:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<ProductCatalogHealthCheck>("product_catalog_health_check");
```

You'll need to add a `using` statement for the namespace where your health check class is defined.

```csharp
using CloudStore.ProductsApi;
```

This will add our custom health check to the existing health checks for the `productsapi` service.

To validate this, start the `productsapi` service and navigate to the dashboard. You should see the `productsapi` service listed in the Resources view, and its health status should be displayed. If you have products in your database, it should show as *Healthy*. If not, it will show as *Unhealthy*.

If you want to test the unhealthy state, you can temporarily remove all products from your database and refresh the dashboard. You should see the health status change to *Unhealthy*. Or set a breakpoint in the `CheckHealthAsync` method and change the `productsExists` condition to `false` to simulate a database error.

If the health check fails, the Aspire Dashboard will show the health status as *Unhealthy*, but you will get a cryptic message.

> Discover endpoint #0 is not responding with code in 200...200 range, the current status is ServiceUnavailable.

This is because Aspire, currently, does not "see" the health check results that are not registered in the AppHost.

## Step 2 – Health Check UI

### 2a – Add HealthChecksUI

Stop the projects and open the `CloudStore.AppHost` project. We will add the HealthChecksUI to the AppHost so that it can display the health status of all services in a single dashboard.

Add the following files from `src/section-02/lab-06-files` to the `CloudStore.AppHost` project:

- `HealthChecksUIExtensions.cs` - This file defines how the resource is created.
- `HealthChecksUIResource.cs` - This file contains properties for the HealthChecksUI resource.

### 2b – Add HealthChecksUI to the AppHost

Add HealthChecksUI to the AppHost. Open `CloudStore.AppHost\Program.cs` and add the HealthChecksUI registration after the `webfrontend` registration:

```csharp
var healthChecksUI = builder.AddHealthChecksUI("healthchecksui")
    .WithReference(apiService)
    .WithReference(productsApi)
    .WithFriendlyUrls("Health Checks UI Dashboard")
   
    // This will make the HealthChecksUI dashboard available from external networks when deployed.
    // In a production environment, you should consider adding authentication to the ingress layer
    // to restrict access to the dashboard.
    .WithExternalHttpEndpoints();
```

Build and run the AppHost. Navigate to the Aspire Dashboard and you should see a new resource called `HealthChecksUI`. Click the `Health Checks UI Dashboard` link to open the dashboard, where you should see the health status of `apiservice` and `productsapi`. However, they are in an *Unhealthy* state because you are not yet reporting detailed status to HealthChecks UI.

At this point, the application is configured to use the HealthChecksUI, but we still need to configure the individual services to report their health check results to the HealthChecksUI.

### 2c – Configure the apps to report to HealthChecks UI

To make HealthChecks UI useful, each service must expose a UI-friendly health endpoint that returns detailed JSON output, and that endpoint should be protected so it is not broadly accessible.

In this step, you update shared defaults so all services get consistent behavior:

- Add **UI response formatting** so HealthChecks UI can parse and display check details.
- Add **timeouts** so slow checks do not hang indefinitely.
- Add **output caching** to reduce repeated probe load.
- Keep the existing readiness/liveness endpoints (`/health` and `/alive`).
- Add a **host-restricted UI endpoint** path (from `HEALTHCHECKSUI_URLS`) so only configured hosts can query detailed health data.

Stop the application.

Add the package `AspNetCore.HealthChecks.UI.Client` to the `CloudStore.ServiceDefaults.csproj` file:

```bash
dotnet add CloudStore.ServiceDefaults\CloudStore.ServiceDefaults.csproj package AspNetCore.HealthChecks.UI.Client
```

The code changes to `CloudStore.ServiceDefaults\Extensions.cs` are shown below. If you prefer, you can copy the `Extensions.cs` file from `lab-06-files` to `CloudStore.ServiceDefaults`.

Open `CloudStore.ServiceDefaults\Extensions.cs` and add the following using statement:

```csharp
using Microsoft.Extensions.Configuration;
using HealthChecks.UI.Client;
```

Replace the `AddDefaultHealthChecks` and `MapDefaultEndpoints` methods with the following code:

```csharp

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        var healthChecksConfiguration = builder.Configuration.GetSection("HealthChecks");

        // All health checks endpoints must return within the configured timeout value (defaults to 5 seconds)
        var healthChecksRequestTimeout = healthChecksConfiguration.GetValue<TimeSpan?>("RequestTimeout") ?? TimeSpan.FromSeconds(5);
        builder.Services.AddRequestTimeouts(timeouts => timeouts.AddPolicy("HealthChecks", healthChecksRequestTimeout));

        // Cache health checks responses for the configured duration (defaults to 10 seconds)
        var healthChecksExpireAfter = healthChecksConfiguration.GetValue<TimeSpan?>("ExpireAfter") ?? TimeSpan.FromSeconds(10);
        builder.Services.AddOutputCache(caching => caching.AddPolicy("HealthChecks", policy => policy.Expire(healthChecksExpireAfter)));

        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        var healthChecks = app.MapGroup("");

        // Configure health checks endpoints to use the configured request timeouts and cache policies
        healthChecks
            .CacheOutput(policyName: "HealthChecks")
            .WithRequestTimeout(policyName: "HealthChecks");

        // All health checks must pass for app to be considered ready to accept traffic after starting
        healthChecks.MapHealthChecks(HealthEndpointPath);

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        healthChecks.MapHealthChecks(AlivenessEndpointPath, new()
        {
            Predicate = r => r.Tags.Contains("live")
        });

        // Add the health checks endpoint for the HealthChecksUI
        var healthChecksUrls = app.Configuration["HEALTHCHECKSUI_URLS"];
        if (!string.IsNullOrWhiteSpace(healthChecksUrls))
        {
            var pathToHostsMap = GetPathToHostsMap(healthChecksUrls);

            foreach (var path in pathToHostsMap.Keys)
            {
                // Ensure that the HealthChecksUI endpoint is only accessible from configured hosts, e.g. localhost:12345, hub.docker.internal, etc.
                // as it contains more detailed information about the health of the app including the types of dependencies it has.

                healthChecks.MapHealthChecks(path, new() { ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse })
                    // This ensures that the HealthChecksUI endpoint is only accessible from the configured health checks URLs.
                    // See this documentation to learn more about restricting access to health checks endpoints via routing:
                    // https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0#use-health-checks-routing
                    .RequireHost(pathToHostsMap[path]);
            }
        }

        return app;
    }
```

Add the following private method to the `Extensions.cs` file:

```csharp
    private static Dictionary<string, string[]> GetPathToHostsMap(string healthChecksUrls)
    {
        // Given a value like "localhost:12345/healthz;hub.docker.internal:12345/healthz" return a dictionary like:
        // { { "healthz", [ "localhost:12345", "hub.docker.internal:12345" ] } }

        var uris = healthChecksUrls.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(url => new Uri(url, UriKind.Absolute))
            .GroupBy(uri => uri.AbsolutePath, uri => uri.Authority)
            .ToDictionary(g => g.Key, g => g.ToArray());

        return uris;
    }
```

### 2d – Validate HealthChecksUI

Start the application, and navigate to the Aspire Dashboard. You should see the `HealthChecksUI` resource listed. Click on the `Health Checks UI Dashboard` link to open the HealthChecksUI dashboard. You should see the health status of the `apiservice` and `productsapi` services. If everything is working correctly, both services should show as *Healthy*.

You'll also notice that for the `productsapi` service, the health check we added earlier, `product_catalog_health_check`, is now being reported to the HealthChecksUI dashboard. If you remove all products from the database and refresh the dashboard, you should see the health status change to *Unhealthy*. It also lists the status of `redis` ("StackExchange.Redis") and `sqlserver` ("AppDbContext") dependencies, which are automatically added because they are a dependency of the `productsapi` service.

---

## Expected Outcome

- `HealthChecksUI` appears as a resource in the Aspire Dashboard.
- `apiservice` and `productsapi` report detailed health results in the HealthChecks UI dashboard.
- `product_catalog_health_check` transitions between Healthy/Unhealthy based on catalog data.
- Default `/health` and `/alive` endpoints continue to behave correctly for readiness/liveness.

---

## Troubleshooting

Use this table to quickly diagnose the most common issues in this lab.

| Symptom                                                                                        | Likely cause                                            | Fix                                                                                                                                                |
| ---------------------------------------------------------------------------------------------- | ------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `productsapi` shows *Unhealthy* in HealthChecks UI with `product_catalog_health_check` failing | Product table is empty                                  | Seed at least one product record, then refresh HealthChecks UI.                                                                                    |
| Aspire Dashboard shows a generic health error (for example, endpoint not returning 2xx)        | Health endpoint is failing or timing out                | Open the service logs in Aspire Dashboard, verify `/health` responds, and confirm dependencies (SQL/Redis) are running.                            |
| HealthChecks UI resource appears, but service entries remain unavailable                       | Services are not exposing UI-formatted health responses | Confirm `AspNetCore.HealthChecks.UI.Client` is installed and `UIResponseWriter.WriteHealthCheckUIResponse` is configured in `MapDefaultEndpoints`. |
| HealthChecks UI cannot read the detailed endpoint                                              | Host filtering blocks the request                       | Verify `HEALTHCHECKSUI_URLS` is set by AppHost and that `RequireHost(...)` includes the actual host/port being used.                               |
| Build fails after adding `ProductCatalogHealthCheck`                                           | Missing namespace import in `Program.cs`                | Add the correct `using` statement for the namespace containing `ProductCatalogHealthCheck`.                                                        |
| Custom health check never changes state while debugging                                        | Check not being hit or stale cached result              | Set a breakpoint in `CheckHealthAsync`, then refresh UI after cache expiration (default 10 seconds) or temporarily lower cache duration.           |

---

## Summary

In this lab, you added application-level and UI-level health visibility across your Aspire solution:

| Area                         | What you implemented                                                                                                      |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| **Custom health check**      | Created `ProductCatalogHealthCheck` to verify product catalog data in `productsapi`                                       |
| **Service registration**     | Registered the custom check with ASP.NET Core health checks in `Program.cs`                                               |
| **AppHost integration**      | Added `HealthChecksUI` as an Aspire resource and linked it to dependent services                                          |
| **Shared endpoint behavior** | Updated `CloudStore.ServiceDefaults` to support UI response output, timeout/cache policies, and host-restricted endpoints |
| **Validation**               | Verified health states, including dependency checks (Redis/SQL), in the HealthChecks UI dashboard                         |

You now have a practical pattern for moving from basic endpoint liveness checks to richer, dependency-aware health reporting in Aspire.

➡️ **Next:** [Lab 7 – Custom Resource Commands](./lab-07-custom-resource-commands.md)

### Reference Links

- [Aspire - Health Checks UI Integration](https://aspire.dev/reference/samples/health-checks-ui/)
- [AspNetCore.Diagnostics.HealthChecks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)
- [HealthChecks UI](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks)
