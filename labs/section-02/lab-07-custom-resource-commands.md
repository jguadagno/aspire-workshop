# Lab 7 – Custom Resource Commands

**Estimated time:** 20 minutes  
**Difficulty:** Beginner–Intermediate  
**Prerequisites:** Completed [Lab 6](./lab-06-custom-health-checks.md) — all services running
**Required Software:** [Workshop Requirements](../../requirements.md)

---

## Objectives

By the end of this lab you will:

- ✅ Add a custom dashboard command to a Redis resource in the AppHost
- ✅ Implement command execution logic that runs `FLUSHALL` against Redis
- ✅ Control command availability based on resource health (`Enabled` vs `Disabled`)
- ✅ Wire the command into the Redis resource configuration with `.WithClearCommand()`
- ✅ Validate command behavior end-to-end using the Aspire Dashboard and Redis Insight
- ✅ Confirm command execution through logs and traces

---

## Background

In Aspire, resources in the AppHost can expose custom actions directly in the Dashboard. These actions are called **resource commands**. They let you run operational tasks (for example, clearing a cache, seeding data, or running diagnostics) without leaving the local development experience.

In this lab, you add a `Clear Cache` command to the Redis resource. The command is implemented in AppHost code and appears in the Redis resource's **Actions** menu in the Dashboard.

The command has three important pieces:

1. **Command registration** (`WithClearCommand`) defines the name, display label, icon, and handler.
2. **Command execution** (`OnRunClearCacheCommandAsync`) connects to Redis and runs `FLUSHALL`.
3. **Command state updates** (`OnUpdateResourceState`) enables the command only when the Redis resource is healthy.

After running the command, you verify the effect in Redis Insight (keys are removed), then observe cache repopulation when requests hit the app again. Logs and traces in the Aspire Dashboard confirm the command ran and that new Redis activity occurred afterward.

---

## Step 1 – Custom Resource Commands

Before writing code, it helps to understand the flow:

1. You register a command in AppHost using `WithCommand(...)` on a specific resource builder.
2. Aspire surfaces that command in the Dashboard under the resource's **Actions** menu.
3. When you click the command in the Dashboard, Aspire executes the handler you provided.
4. Aspire calls your `UpdateState` callback to decide whether the command should be enabled or disabled.

This gives you a clean way to attach operational actions directly to a resource while keeping the logic close to infrastructure orchestration in AppHost.

In the `CloudStore.AppHost` project, you can define custom commands that appear in the Resources view for each service. This allows you to trigger specific actions (e.g., clear cache, seed database) directly from the dashboard and see the results in the logs/traces.

Create a custom command to clear the Redis cache in the `cache` resource.

In the `CloudStore.AppHost` project, create a new file named `RedisResourceBuilderExtensions.cs`, or copy the file from `.\lab-07-files\RedisResourceBuilderExtensions.cs`, with the following content:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aspire.Hosting;

internal static class RedisResourceBuilderExtensions
{
    public static IResourceBuilder<RedisResource> WithClearCommand(
        this IResourceBuilder<RedisResource> builder)
    {
        var commandOptions = new CommandOptions
        {
            UpdateState = OnUpdateResourceState,
            IconName = "AnimalRabbitOff",
            IconVariant = IconVariant.Filled
        };

        builder.WithCommand(
            name: "clear-cache",
            displayName: "Clear Cache",
            executeCommand: context => OnRunClearCacheCommandAsync(builder, context),
            commandOptions: commandOptions);

        return builder;
    }

    private static async Task<ExecuteCommandResult> OnRunClearCacheCommandAsync(
        IResourceBuilder<RedisResource> builder,
        ExecuteCommandContext context)
    {
        var connectionString = await builder.Resource.GetConnectionStringAsync() ??
            throw new InvalidOperationException(
                $"Unable to get the '{context.ResourceName}' connection string.");

        await using var connection = ConnectionMultiplexer.Connect(connectionString);
        var database = connection.GetDatabase();
        await database.ExecuteAsync("FLUSHALL");

        return CommandResults.Success();
    }

    private static ResourceCommandState OnUpdateResourceState(
        UpdateCommandStateContext context)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<Program>>();
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Updating resource state: {ResourceSnapshot}",
                context.ResourceSnapshot);
        }

        return context.ResourceSnapshot.HealthStatus is HealthStatus.Healthy
            ? ResourceCommandState.Enabled
            : ResourceCommandState.Disabled;
    }
}
```

How this ties into AppHost and the Dashboard:

- `WithClearCommand`: This is the extension method you call from AppHost when configuring Redis. It registers a new dashboard command named `clear-cache` with a display label (`Clear Cache`), icon metadata, and the execution callback.
- `OnRunClearCacheCommandAsync`: This is the command handler Aspire executes when you click **Clear Cache** in the Dashboard. It resolves the Redis connection string from the resource, connects, and runs `FLUSHALL`.
- `OnUpdateResourceState`: This callback runs when Aspire evaluates command availability. It returns `Enabled` only when the Redis resource health is `Healthy`, which prevents running the command when Redis is unavailable.

Together, these methods provide registration, execution, and safety gating for the command lifecycle.

It's important that the namespace stays as `Aspire.Hosting` so that the AppHost can find this extension method.

Now in the `AppHost.cs` file, we need to call this new extension method when configuring the Redis resource. Find the section where the Redis resource is added and update it to look like this:

```csharp
var cache = builder.AddRedis("cache")
    .WithClearCommand()
    .WithRedisInsight();
```

Now when you start the AppHost and navigate to the Aspire dashboard, click on the **...** in the **Actions** column of the `redis` resource. You'll see a "Clear Cache" button. Clicking this button will trigger the `FLUSHALL` command on the Redis instance, clearing all cached data.

Test it out by first loading the Products page in the web app to populate the cache:

- Navigate to the Products page in the web app
- Navigate back to the Aspire dashboard
- Click on the URL for the `redisinsight` resource, to open up the Redis Insight dashboard
- Click on the `cache` database alias
- You should see a key similar to `__MSOCV_GET_HTTP_LOCALHOST` with a child key like `__MSOCV_GET_HTTP_LOCALHOST:5114/PRODUCTS_Q_*=`. This is the cache entry for the products API response.
- Go back to the Aspire dashboard, click the **...** in the **Actions** column of the `redis` resource
- Click "Clear Cache" in the dashboard.
- Refresh the Redis Insight `cache` database in the dashboard — the cache key should now be gone, confirming that the cache was cleared successfully.
- Visit the Products page in the web app again — this will be a cache miss since we cleared the cache, and a new cache entry will be created in Redis.
- If you want, you can look at the console logs for the `cache` resource in the Aspire dashboard to see the logs for the command execution. You should see a `Successfully executed command 'clear-cache'.` log entry confirming the command ran successfully.
- You will also see a trace that reloads the cache from the database. There will be a span in the trace `webfrontend: GET /products` with a child span `DATA redis SETEX`.

---

## Expected Outcome

- A `Clear Cache` action is available for the Redis resource in the Aspire Dashboard.
- The command is enabled only when Redis is healthy.
- Running `Clear Cache` removes existing cache keys from Redis (visible in Redis Insight).
- Reloading the Products page recreates Redis cache entries.
- Dashboard logs include a successful command execution message.
- Dashboard traces show Redis activity (for example, `DATA redis SETEX`) after cache repopulation.

---

## Troubleshooting

Use this table to quickly diagnose common issues in this lab.

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| `Clear Cache` does not appear in the Redis resource Actions menu | `.WithClearCommand()` was not added to the Redis resource configuration | Update AppHost Redis configuration to include `.WithClearCommand()` before running the app. |
| Build error: extension method not found for `.WithClearCommand()` | Extension class namespace or file placement is incorrect | Ensure `RedisResourceBuilderExtensions.cs` is in AppHost and uses the `Aspire.Hosting` namespace. |
| `Clear Cache` is visible but disabled | Redis resource health is not `Healthy` | Check Redis container/resource status in Dashboard and resolve startup/connection issues first. |
| Command fails at runtime with connection string error | Redis connection string could not be resolved | Verify Redis resource is running and referenced correctly in AppHost so `GetConnectionStringAsync()` can resolve it. |
| Command runs but keys still appear in Redis Insight | Redis Insight view is stale or connected to wrong instance | Refresh Redis Insight and confirm you are inspecting the `cache` database for the current app environment. |
| No new cache key appears after clearing and reloading Products | Request path did not trigger cached endpoint, or app did not reload | Reload the Products page again and verify `productsapi` is healthy; then refresh Redis Insight. |

---

## Summary

In this lab, you extended Aspire's AppHost with a custom operational command for Redis and validated it through the dashboard experience.

| Area | What you implemented |
| --- | --- |
| **Resource command registration** | Added a `clear-cache` command with label/icon via `WithCommand(...)` |
| **Command execution** | Implemented `OnRunClearCacheCommandAsync` to connect to Redis and run `FLUSHALL` |
| **Command state logic** | Implemented `OnUpdateResourceState` to enable the command only when Redis is healthy |
| **AppHost integration** | Wired command support into Redis with `.WithClearCommand()` |
| **Validation** | Verified key removal in Redis Insight and cache repopulation via app traffic |
| **Observability confirmation** | Confirmed command and cache behavior with dashboard logs/traces |

You now have a reusable pattern for adding safe, resource-scoped operational commands to Aspire.
