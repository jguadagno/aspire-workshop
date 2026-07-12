# Lab 1 – Your First Aspire Application

**Estimated time:** 20 minutes  
**Difficulty:** Beginner
**Required Software:** [Workshop Requirements](../../requirements.md)

---

## Objectives

By the end of this lab you will:

* ✅ Install the Aspire CLI
* ✅ Create a new Aspire starter solution
* ✅ Understand the role of each project in the solution
* ✅ Run the application and open the Aspire dashboard
* ✅ Navigate the Resources, Logs, and Traces views
* ✅ Trace an HTTP request end-to-end

---

## Background

Aspire is an opinionated, cloud-ready stack for building distributed applications. It gives you:

* **App Host** – orchestrates all your services locally (like a local `docker-compose`, but .NET-native)
* **Service Defaults** – a shared library that wires up OpenTelemetry, health checks, and service discovery for every project
* **Aspire Dashboard** – a local observability UI showing logs, traces, and metrics in real time

---

## Step 1 – Install Aspire CLI

If you have not already installed Aspire CLI, open a command prompt or terminal and run:

```powershell
irm https://aspire.dev/install.ps1 | iex
```

Or install with the .NET CLI:

```powershell
dotnet tool install -g Aspire.Cli
dotnet new install Aspire.ProjectTemplates@13.4.2 # Change the 13.4.2 to the latest version if needed
```

Verify the installation:

```powershell
aspire --version
```

## Step 2 – Create the CloudStore Solution

In your terminal, navigate to a directory where you want to create the solution. Then run the following commands to create a new Aspire starter solution called `CloudStore`:

```bash
dotnet new aspire-starter --name CloudStore --output CloudStore
cd CloudStore
```

This generates four projects:

```text
CloudStore/
├── CloudStore.AppHost/          # Orchestrator – entry point for local dev
├── CloudStore.ServiceDefaults/  # Shared OpenTelemetry & health check helpers
├── CloudStore.ApiService/       # Sample minimal API (weather forecast)
└── CloudStore.Web/              # Blazor Server frontend
```

## Step 3 – Explore the Solution Structure

Open the solution in your editor:

```bash
# Visual Studio Code
code .

# Visual Studio
start CloudStore.sln
```

> Note: If prompted to Trust the authors, click "Yes" to enable full functionality.

### 3a – AppHost (`CloudStore.AppHost/Program.cs`)

The AppHost is the entry point for local development. It orchestrates all the services in the solution, manages their lifecycle, and provides shared features like service discovery and telemetry.

The AppHost code should look like this:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.CloudStore_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.CloudStore_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
```

**Key concepts:**

* `AddProject<T>` registers a .NET project as a resource
* `.WithExternalHttpEndpoints()` exposes all HTTP endpoints to the host machine (not just localhost) and registers them in the dashboard
* `.WithHttpHealthCheck("/health")` configures the dashboard health status based on the specified endpoint
* `WithReference` injects the service URL as an environment variable
* `WaitFor` delays startup until the dependency is healthy

### 3b – ServiceDefaults (`CloudStore.ServiceDefaults/Extensions.cs`)

```csharp
public static IHostApplicationBuilder AddServiceDefaults(
    this IHostApplicationBuilder builder)
{
    builder.ConfigureOpenTelemetry();      // traces + metrics to dashboard
    builder.AddDefaultHealthChecks();       // /health and /alive endpoints
    builder.Services.AddServiceDiscovery(); // resolve "http://apiservice"
    // ...
    return builder;
}
```

Every service project calls `builder.AddServiceDefaults()` so telemetry is consistent.

### 3c – ApiService (`CloudStore.ApiService/Program.cs`)

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();  // <-- ServiceDefaults wired in
// ...
app.MapDefaultEndpoints();     // registers /health and /alive
app.MapGet("/weatherforecast", () => { /* ... */ });
```

### 3d – Web Frontend (`CloudStore.Web/Program.cs`)

```csharp
builder.AddServiceDefaults();

// HttpClient that resolves "apiservice" via service discovery
builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new Uri("http://apiservice");
});
```

## Step 4 – Run the Application

From the repo root, run the **AppHost** project (this starts everything):

```powershell
aspire run
```

The "old" way...

```bash
dotnet run --project CloudStore/CloudStore.AppHost
```

Watch the terminal output. You will see something like:

```text
AppHost:  CloudStore.AppHost\CloudStore.AppHost.csproj                       
                                                                                 
Dashboard:  https://localhost:17039/login?t=<token>   
                                                                                  
Logs:  C:\Users\<localuser>\.aspire\logs\cli_20260606T210544_4c316f10.log     
```

## Step 5 – Open the Aspire Dashboard

Copy the dashboard URL from the terminal (including the login token) and open it in your browser.

> 💡 Tip: In many terminals you can Ctrl+Click the URL to open it directly.

## Step 6 – Explore the Dashboard

### Resources View (default landing page)

You should see two resources in the **Running** state:

| Name          | Type    | Endpoints               |
| ------------- | ------- | ----------------------- |
| `apiservice`  | Project | <http://localhost:xxxx> |
| `webfrontend` | Project | <http://localhost:xxxx> |

Click on an endpoint URL to open the service in a new tab.

### Structured Logs View

1. Click **Structured logs** in the left nav
2. Select **apiservice** from the resource filter
3. Notice startup messages with structured key-value pairs

### Traces View

1. Navigate to the Blazor web app in your browser
2. Click the **Weather** page to trigger an API call
3. Go back to the dashboard → **Traces**
4. Find the trace for `GET /weatherforecast`
5. Click it to open the waterfall view — you'll see the request span from the web project and the child span in the API service

## Step 7 – Make a Request and Find Its Trace

1. Open the web frontend and visit the Weather page
2. Switch to the dashboard Traces view
3. Locate the most recent trace — it should span **two services** (web → apiservice)
4. Click the trace to see the full waterfall with timing breakdowns

## Expected Outcome

* Both services show **Running** (green) in the Resources view
* Traces show end-to-end requests crossing service boundaries
* Logs appear in structured format with filterable fields

## Troubleshooting

| Problem                     | Solution                                                       |
| --------------------------- | -------------------------------------------------------------- |
| Docker not running          | Start Docker Desktop and wait for it to initialize             |
| Port already in use         | Stop other running Aspire apps; ports are assigned dynamically |
| Dashboard login fails       | Copy the full URL including the `?t=` token from terminal      |
| Service stays in "Starting" | Check the Logs view for that resource to see the error         |

## ✨ Bonus Challenge

Add a second endpoint to `CloudStore.ApiService` after the `.WithName("GetWeatherForecast")` endpoint:

```csharp
app.MapGet("/products/count", () => new { Count = 42, UpdatedAt = DateTime.UtcNow });
```

1. Re-run the app
2. Call the new endpoint directly (find the URL in the Resources view) it should be something like: `https://localhost:xxxxx/products/count`
3. Locate its trace in the dashboard
4. Notice the auto-generated span name matches the route pattern

## Summary

| Concept                    | Where to see it                                    |
| -------------------------- | -------------------------------------------------- |
| Service orchestration      | `AppHost/Program.cs`                               |
| Shared observability setup | `ServiceDefaults/Extensions.cs`                    |
| Service discovery          | `WithReference` + `http://apiservice` base address |
| End-to-end tracing         | Dashboard → Traces → waterfall view                |

➡️ **Next:** [Lab 2 – Orchestrating Services with the App Host](./lab-02-orchestrating-services-with-the-app-host.md)
