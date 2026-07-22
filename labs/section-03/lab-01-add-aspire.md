# Lab 1 – Adding Aspire to the Starter Application

**Estimated time:** 50 minutes  
**Difficulty:** Beginner  
**Prerequisites:** Completed the starter application setup (can run locally with docker-compose)
**Required Software:** [Workshop Requirements](../../requirements.md)

---

## Objectives

By the end of this lab you will:

- ✅ Create an Aspire AppHost project
- ✅ Register the existing CloudStore.Api and CloudStore.Web projects in the AppHost
- ✅ Run the AppHost and verify both services start together
- ✅ Open the Aspire dashboard and explore the Resources view
- ✅ Verify the API still responds and serves data

---

## Background

The **Aspire AppHost** is a .NET console application that describes your entire distributed system as code. Instead of running services individually with docker-compose, the AppHost:

- **Orchestrates services**: Starts/stops services together
- **Manages ports**: Automatically assigns and resolves ports
- **Enables service discovery**: Injects connection strings automatically
- **Provides observability**: Captures logs, traces, and metrics in real-time

In this lab, we'll create an AppHost that registers our existing API and Web projects, replacing the need to run them manually.

---

## Getting Started

You can begin the lab with the starter application in the `src/section-03/start-here` folder. This application is fully functional and can be run locally with docker-compose. The goal of this lab is to replace the docker-compose setup with an Aspire AppHost.  If you want, make a copy of the starter application in a new folder (e.g., `src/section-03/lab-01`) so you can experiment without affecting the original.

> Note: Make sure you stop any running docker-compose services before starting the lab. You can do this by running `docker compose down` in the starter application folder.
> Note: If you copy the starter application to a new folder after you have already got the project working, it may take a few minutes to copy because of the `node_modules` folder. You can skip copying the `node_modules` folder and run `npm install` in the new folder to restore the dependencies.

## Lab 1 – Add Aspire AppHost (CLI-First)

In this lab you will convert CloudStore from `docker-compose`-managed dependencies to an Aspire AppHost that orchestrates all local runtime dependencies:

- PostgreSQL
- Redis
- Azure Storage emulator via `.AddAzureStorage` (Blob + Table)

You will also register Aspire MCP so you can inspect resources, logs, and traces while the app is running.

## Lab Objective

By the end of this lab, a single `aspire start` command should boot:

- `CloudStore.Api`
- `CloudStore.Web`
- PostgreSQL resource
- Redis resource
- Azure Storage resource (for `product-images` and `ProductImageQueue` usage)

And you should be able to validate everything from the Aspire dashboard and MCP tools.

## Command Conventions

- Run commands from the repository root unless a step says otherwise.
- Commands are written for PowerShell.
- Copy/paste commands in order.

## Step 1 – Prerequisites and workspace safety checks

### 1.1 Verify required tools

Verify the following tools are installed and working:

- dotnet
- node
- npm
- docker
- aspire

If `aspire` is missing, install/update it:

```powershell
dotnet tool update -g Aspire.Cli
```

Then verify again:

```powershell
aspire --version
```

#### Tooling Checkpoint

- `dotnet` reports a .NET 10 SDK.
- Docker is reachable (`docker info` succeeds).
- `aspire --version` returns a version value.

#### Tooling Troubleshooting

- If Docker commands fail, start Docker Desktop and retry.
- If `aspire` is not recognized, restart the terminal after tool installation.

### 1.2 Stop old `docker-compose` runtime to avoid port conflicts

This app previously used:

- PostgreSQL on `5432`
- Redis on `6379`
- Azurite on `10000`, `10001`, `10002`

Stop anything that may still be running from `docker-compose.yml`:

```powershell
docker compose down
docker ps --format "table {{.Names}}\t{{.Ports}}"
```

#### Docker Checkpoint

- `docker compose down` completes without errors.
- You do not see `cloudstore-postgres`, `cloudstore-redis`, or `cloudstore-azurite` running.

#### Docker Troubleshooting

- If the command says no compose project found, continue (that is safe).
- If ports are still occupied by other containers, stop those specific containers before continuing.

### 1.3 Restore solution dependencies before Aspire wiring

```powershell
dotnet restore .\CloudStore.slnx
```

#### Package Restore Checkpoint

- Restore completes successfully with no failed projects.

## Step 2 – Create AppHost and orchestrate PostgreSQL, Redis, and Azure Storage

### 2.1 Initialize Aspire in this existing repo

From the repo root:

```powershell
aspire init
```

- When prompted for the AppHost type, choose a **C# (.NET)**.
- When prompted for the AI agent environments, choose *no* for now (we'll add MCP later).

Add the AppHost project to the solution:

```powershell
dotnet sln add .\CloudStore.AppHost\CloudStore.AppHost.csproj
```

#### Checkpoint

- A new AppHost project is created (for example, `AppHost\AppHost.csproj`).
- `CloudStore.slnx` now includes the AppHost project.

#### Aspire Checkpoint

- AppHost project is correctly initialized and included in the solution.

#### Aspire Troubleshooting

- If init fails due to existing Aspire artifacts, remove incomplete AppHost scaffolding and rerun `aspire init`.
- If your terminal cannot run `aspire`, return to Step 1.1 and fix CLI installation.

### 2.2 Add Aspire integrations for PostgreSQL, Redis, and Azure Storage

From the AppHost project directory:

```powershell
cd CloudStore.AppHost
aspire add postgres # Select "postgressql (Aspire.Hosting.Postgres)" when prompted
aspire add redis
aspire add azure-storage
```

#### Aspire Integrations Checkpoint

- AppHost package references include hosting integrations for PostgreSQL, Redis, and Azure Storage.
- AppHost code can use resource methods for these services.

### 2.3 Model resources in `AppHost\Program.cs`

Update `CloudStore.AppHost\AppHost.cs` to include resource declarations so Aspire orchestrates all dependencies currently represented in `docker-compose.yml`.

We'll see later that the names used here will matter for configuration injection into the API and Web projects. It's important to keep the names consistent with what the API expects.

Replace the contents of `AppHost.cs` with the following code:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("postgres-data")
    .WithPgAdmin();
var cloudStoreDb = postgres.AddDatabase("PostgreSQL", "cloudstore");

var redis = builder.AddRedis("redis");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite =>
    {
        azurite.WithDataVolume("storage-data");
    });
var blobs = storage.AddBlobs("blobs");
var tables = storage.AddTables("tables");

builder.Build().Run();
```

> Why this matters: this mirrors current runtime dependencies (PostgreSQL, Redis, Azurite Blob/Table) while moving lifecycle management into Aspire.

### 2.4 Start Aspire and validate resource health

```powershell
aspire start
```

After startup, open the Aspire dashboard URL printed in the terminal. It will look similar to this:

```text
     AppHost:  CloudStore.AppHost.csproj

   Dashboard:  https://localhost:17298/login?t=d637c204d129963ad661156a4f606a69

        Logs:  <user_home>\.aspire\logs\cli_20260630T162934383_detach-child_8fd6b9664afa4cad9f98ccdeedc85725.log

         PID:  36640

✅ AppHost started successfully.
```

Click the dashboard link and verify that the resources are running and healthy:

- Storage
  - blobs
  - tables
- pgadmin
- postgres
  - PostgreSQL
- redis

#### Start Aspire Dashboard Checkpoint

- You see PostgreSQL, Redis, and Azure Storage resources in the dashboard.
- Resource state transitions to `Running`/healthy.
- No conflicting container port failures appear.

#### Start Aspire Dashboard Troubleshooting

- If a resource is unhealthy, open its logs in the dashboard and verify Docker Desktop is running.
- If startup fails with port conflict, stop conflicting containers/processes and rerun `aspire start`.
- If package restore fails during startup, run `dotnet restore .\CloudStore.slnx` and retry.

## Step 3 – Wire `CloudStore.Api` and `CloudStore.Web` into AppHost and map configuration

Stop the AppHost if it is running, then add the API and Web projects as resources.

```bash
aspire stop
```

### 3.1 Add API and Web projects to AppHost

Stop the AppHost if it is running, then add the API and Web projects as resources.

Reference the API project:

```bash
dotnet add CloudStore.AppHost.csproj reference ../CloudStore.Api/CloudStore.Api.csproj
```

Since the web project is an Angular/Vite app, it does not need a project reference. Instead, we will register it as a Vite app resource in the AppHost. But first we need to add the `Aspire.Hosting.JavaScript` package to the AppHost project:

```bash
dotnet add CloudStore.AppHost.csproj package Aspire.Hosting.JavaScript
```

Now we can add the API and Web resources in `CloudStore.AppHost\AppHost.cs`. Add the following code after the resource declarations for PostgreSQL, Redis, and Azure Storage:

```csharp
var api = builder.AddProject<Projects.CloudStore_Api>("api")
    .WithReference(cloudStoreDb)
    .WithReference(redis)
    .WithReference(blobs)
    .WithReference(tables)
    .WaitFor(cloudStoreDb)
    .WaitFor(redis)
    .WaitFor(blobs)
    .WaitFor(tables);

var web = builder.AddViteApp("web", "../CloudStore.Web", "start")
    .WithReference(api)
    .WaitFor(api);
```

#### Adding API/Web Checkpoint

- AppHost now includes both app resources: API and Web.
- API has references to PostgreSQL, Redis, and Azure Storage resources.
- Web has a reference to API.

### 3.2 Map API configuration expectations to Aspire resource injection

Current API behavior (`CloudStore.Api\Program.cs`) expects these connection strings:

- `ConnectionStrings:PostgreSQL`
- `ConnectionStrings:Redis`
- `ConnectionStrings:AzureStorage`

You can keep the `CloudStore.Api\appsettings.json` as local fallback, but I recommend that you delete it temporarily or rename the settings to make sure your connections are correctly being loaded from Aspire.

Aspire will inject the connection strings at runtime. The configuration keys will match the resource names you used in `AppHost.cs`.

So,

```csharp
var cloudStoreDb = postgres.AddDatabase("PostgreSQL", "cloudstore" );
```

will inject the PostgreSQL connection string as `ConnectionStrings:PostgreSQL`. Similarly, Redis and Azure Storage will be injected as `ConnectionStrings:Redis`, respectively.

This means, in our current application, we need to tweak a few things to make sure the API reads the connection strings from the environment instead of hardcoding them in `appsettings.json`.

1) You need to make sure the connection strings in *CloudStore.Api\appsettings.json* are removed or commented out, so that the API does not use them. Aspire will inject the connection strings at runtime.
2) You need to make sure the connection strings names in *CloudStore.Api\Program.cs* match the resource names you used in *AppHost.cs*. For example, if you used `redis` as the name of the redis resource in `AppHost.cs`, then the connection string key in `Program.cs` should be `ConnectionStrings:redis`.

Double-check the connection string names in `CloudStore.Api\Program.cs`:

#### PostgreSQL Configuration

Since we used the *name*, property of the `.AddDatabase` method as `PostgreSQL`, the connection string key will be `ConnectionStrings:PostgreSQL`. So, in `Program.cs`, we should be good keeping the connection string key as `ConnectionStrings:PostgreSQL` and the code untouched.

However, if you want to use the power of Aspire and add additional health checks to your database connection, **recommended**, you can use the Aspire PostgreSQL Entity Framework Core package to register the DbContext with the dependency injection system and add a health check for the database connection. To do this, you need to add the following NuGet package to the API project:

```powershell
dotnet add ../CloudStore.Api/CloudStore.Api.csproj package Aspire.Azure.Npgsql.EntityFrameworkCore.PostgreSQL
```

Then, in the `CloudStore.Api\Program.cs`, you can register the DbContext with the dependency injection system using the `AddPostgresDbContext` method. Locate the `// Database` comment in the `CloudStore.Api\Program.cs` file and replace the four lines after it, with the following code:

```csharp
builder.AddAzureNpgsqlDbContext<CloudStoreDbContext>(connectionName: "PostgreSQL");
```

#### Redis Configuration

Since we set the *name* of the `.AddRedis` method as `redis`, the connection string key will be `ConnectionStrings:redis`. Since the ConnectionStrings and keys in the .NET configuration system are not case sensitive, you can use either `redis` or `Redis` as the connection string key. So, in `Program.cs`, we should be good keeping the connection string key as `ConnectionStrings:Redis` and the code untouched.

#### Azure Storage Configuration

The Azure storage connections work a little differently. Aspire will create a connection string for each of the storage services/resources (Blob, Table, Queue) you add to the emulator and inject them into configuration system. The connection string keys will be based on the resource names you used in `AppHost.cs`. They take the form of `ConnectionStrings:<resource-name>`. For example, if you added a Blob storage resource with the name `blobs`, the connection string key will be `ConnectionStrings:blobs`. If you added a Table storage resource with the name `tables`, the connection string key will be `ConnectionStrings:tables`.

You have two options at this point:

1) You can change the connection string keys in `Program.cs` to match the resource names you used in `AppHost.cs`. For example, if you used `blobs` as the name of the Blob storage resource, you would change the connection string key in `Program.cs` to `ConnectionStrings:blobs`. Similarly, if you used `tables` as the name of the Table storage resource, you would change the connection string key in `Program.cs` to `ConnectionStrings:tables`. *Not recommended*, because it is less flexible and requires you to change the connection string keys in `Program.cs` if you change the resource names in `AppHost.cs`.
2) If you use the Aspire helper packages or *Aspire.Azure.Storage.Blobs* and *Aspire.Azure.Data.Tables*, you can use the `.AddAzureBlobClient` and `.AddAzureTableClient` methods, respectively, to register the Blob and Table clients with the dependency injection system, and "pick up" the connection string based on the *name* parameter. *Recommended*, because it is more flexible and allows you to use the same code in different environments (local, staging, production) without changing the connection string keys in `Program.cs`. You can also use the same code in different projects without having to change the connection string keys.

I prefer option 2, because it is more flexible and allows you to use the same code in different environments (local, staging, production) without changing the connection string keys in `Program.cs`. You can also use the same code in different projects without having to change the connection string keys.

First add the following NuGet packages to the API project:

```powershell
dotnet add ../CloudStore.Api/CloudStore.Api.csproj package Aspire.Azure.Storage.Blobs
dotnet add ../CloudStore.Api/CloudStore.Api.csproj package Aspire.Azure.Data.Tables
```

Now in the `CloudStore.Api\Program.cs`, you can register the Blob and Table clients with the dependency injection system using the `AddAzureBlobClient` and `AddAzureTableClient` methods, respectively.

Locate the `// Azure Storage (Azurite emulator)` comment in the `CloudStore.Api\Program.cs` file, then replace all of the code up to the `// Services` comment with the following code:

```csharp
builder.AddAzureBlobServiceClient("blobs");
builder.AddAzureTableServiceClient("tables");
```

> Note: The names `blobs` and `tables` must match the resource names you used in `AppHost.cs`. If you used different names, change them accordingly.

Now, the API will pick up the connection strings for Blob and Table storage from Aspire at runtime, and you do not need to hardcode them in `Program.cs`. However, you still need to update the `StorageService` to use the `BlobServiceClient` and `TableServiceClient` that are registered with the dependency injection system.

Open up the `CloudStore.Infrastructure\Services\StorageService.cs` file and update the constructor to accept `BlobServiceClient` and `TableServiceClient` as parameters. Then, use these clients to perform Blob and Table operations.

```csharp
public class StorageService(BlobServiceClient blobServiceClient, TableServiceClient tableServiceClient, IImageService imageService) : IStorageService
```

Next get a instance of the Blob container `product-images` for the `UploadProductImageAsync` method.

Right after the `blobName` variable is defined, add the following code to get a reference to the Blob container:

```csharp
var blobContainer = blobServiceClient.GetBlobContainerClient(ContainerName);
```

Now update the `UploadProductImageAsync` method to get a Table client for the `ProductImageQueue` table. Just before the `await queueTable.CreateIfNotExistsAsync();` line, add the following code to get a reference to the Table client:

```csharp
var queueTable = tableServiceClient.GetTableClient(TableName);
```

Finally, in the `DeleteProductImageAsync` method, we need to get a reference to the Blob container as well. Just before the `var blob = blobContainer.GetBlobClient(blobName);` line, add the following code to get a reference to the Blob container:

```csharp
var blobContainer = blobServiceClient.GetBlobContainerClient(ContainerName);
```

Now the `StorageService` is using the `BlobServiceClient` and `TableServiceClient` that are registered with the dependency injection system, and it will pick up the connection strings from Aspire at runtime.

#### Map Azure Storage Configuration Checkpoint

- You do **not** hardcode host/port values inside AppHost for these dependencies.
- The API starts successfully under Aspire and can apply migrations at startup.

### 3.3 Update Web API URL strategy (remove hardcoded localhost API URL)

The Angular frontend currently hardcodes the API URL to `https://localhost:7200/api/products`. This will break when running under Aspire, because the API may be on a different port or host.

First, we must get the API base URL from the AppHost runtime configuration.

Edit the `CloudStore.AppHost\AppHost.cs` file to add a runtime configuration for the API base URL. Add the following code for `web` resource after the `.WithReference(api)` and before the `.WaitFor(api)`.

```csharp
    .WithEnvironment("VITE_API_BASE_URL", api.GetEndpoint("http"))
```

This will inject the API base URL into the Web frontend as an environment variable `VITE_API_BASE_URL`. The Angular app can then read this value at runtime instead of hardcoding it.

Now replace the API URL from the AppHost runtime config.

`CloudStore.Web\src\app\services\product.service.ts` currently hardcodes:

```typescript
private apiUrl = 'https://localhost:7200/api/products';
```

Replace it so Web reads API base URL from environment/runtime config provided by AppHost (for example via `import.meta.env` or a generated config endpoint).

```typescript
private apiUrl =`${import.meta.env.VITE_API_BASE_URL}/api/products`;
```

You may need to create a new file, `vite-env.d.ts`, in the `src` folder of the *Web* project to include the new environment variable:

```typescript
interface ImportMetaEnv {
    readonly VITE_API_BASE_URL: string
}

interface ImportMeta {
    readonly env: ImportMetaEnv
}
```

Once you do this, the Web frontend will use the API base URL provided by Aspire at runtime, and it will work regardless of the port or host that the API is running on.

However, the frontend will still not work since we have a CORS policy that only allows `https://localhost:4200` to access the API. We need to update the CORS policy in `CloudStore.Api\Program.cs` to allow the Web frontend to access the API when running under Aspire. But first, we need to get the Web frontend URL from the AppHost runtime configuration.

Open up the `CloudStore.AppHost\AppHost.cs` file and add a runtime configuration for the Web frontend URL. Add the following code for `api` resource after the declaration of the `web` variable.

```csharp
api.WithEnvironment("Angular_FrontEnd", web.GetEndpoint("http"));
```

The name, `Angular_FrontEnd`, is arbitrary, but it you need to remember it because we are going to use it in the *Api* project to get the web frontend URL at runtime.

Open up the `CloudStore.Api\Program.cs` file and update the CORS policy to allow the Web frontend to access the API when running under Aspire. Replace the hardcoded `https://localhost:4200` with the runtime configuration value for the Web frontend URL. change the `policy.WithOrigins` line to the following:

```csharp
        var frontEndUri = Environment.GetEnvironmentVariable("Angular_FrontEnd") ?? "http://localhost:4200";
        policy.WithOrigins(frontEndUri)
            .AllowAnyHeader()
            .AllowAnyMethod();
```

This will allow the Web frontend to access the API when running under Aspire, and it will also allow the Web frontend to access the API when running locally with `docker-compose` or in production.

Now, you can start the AppHost and verify that the Web frontend can access the API and perform CRUD operations.

#### Update Web API URL Strategy Checkpoint

- Web no longer depends on a fixed `https://localhost:7200` URL.
- Running with Aspire routes frontend API calls to the AppHost-managed API endpoint.

### 3.4 Validate end-to-end behavior through Aspire

From repo root:

```powershell
aspire start
```

Open the frontend endpoint from the Aspire dashboard and verify:

1. Product list loads.
2. Create/update/delete operations succeed.
3. Image upload succeeds (Blob container: `product-images`).
4. Queue table writes succeed (`ProductImageQueue`).

#### Validation Troubleshooting

- If Web cannot reach API, inspect Web environment variables and API endpoint in dashboard.
- If API starts but CRUD fails, check API logs for database connection errors.
- If image upload fails, check Azure Storage resource logs and API exceptions.
- If you uploaded an image but it does not appear in the product list, this is most likely due to the permissions of the blob container. Make sure the container is set to allow public access to blobs. You can do this in the Azure Storage Explorer. Note: you will not want to make your container public in production, you will want to put it behind a CDN, but for this lab, it is fine to do so.

### 3.5 Service Defaults

Now that everything is working, you can add the "service defaults" functionality to the application. This will allow you to add telemetry, health checks, and service discovery to the application without having to modify the code in the API or Web projects.

Stop the AppHost if it is running..

In a Terminal, run the following command from the repo root:

```powershell
cd .. # repository root
dotnet new aspire-servicedefaults -n CloudStore.ServiceDefaults
dotnet sln add .\CloudStore.ServiceDefaults\CloudStore.ServiceDefaults.csproj
```

Add a reference to the `CloudStore.ServiceDefaults` project in the `CloudStore.Api` project:

```powershell
dotnet add CloudStore.Api/CloudStore.Api.csproj reference CloudStore.ServiceDefaults/CloudStore.ServiceDefaults.csproj
```

Next in the `CloudStore.Api\Program.cs` file, add the call to the `AddServiceDefaults` method after the `builder` variable is created:

```csharp
builder.AddServiceDefaults();
```

Now we need to map the endpoints for the health checks. Further down in the `CloudStore.Api\Program.cs` file, add the following code after the `var app = builder.Build();` line:

```csharp
app.MapDefaultEndpoints();
```

At this point, the API will have telemetry, health checks, and service discovery enabled. You can verify this by checking the logs in the Aspire dashboard.

#### Service Defaults Checkpoint

You can validate that the health checks are working by opening the health check endpoint in a browser or using a tool like Postman. The health check endpoint is available at `https://localhost:7200/health` or `https://localhost:7200/alive`. You should see a JSON response indicating that the API is healthy.

Stop the AppHost.

## Step 4 – Register Aspire MCP and run validation + troubleshooting workflow (Optional)

Aspire provides an experience for AI agents to inspect and interact with running AppHost instances. This is done through the Aspire MCP (Model Context Protocol) commands.

### 4.1 Install the Aspire MCP agent and skills

Stop the AppHost if it is running, then register the AppHost with Aspire MCP:

```powershell
aspire stop
aspire agent init
```

When prompted for the path to the root of your workspace, provide the path of the repository root (for example, `D:\Projects\aspire-workshop\section-03\lab-01`).  If you run this command from the repository root, you can just press Enter to accept the default path.

Depending on the environment you are running this command from, you may be prompted to select the AI agent environment. If you are prompted, select the default option.

```text
> [X] Standard (.agents/skills/) — Supported by VS Code, GitHub Copilot, and OpenCode (also installs at ~/) 
  [ ] Claude Code (.claude/skills/) — Required for Claude Code                                              
  [ ] VS Code / GitHub Copilot (.github/skills/) — Legacy location for GitHub Copilot skills                
  [ ] OpenCode (.opencode/skill/) — Legacy location for OpenCode skills   
```

*Standard* is the recommended option for this lab.

Select the skills that you want to enable for this agent. Make sure you scroll down and select the `Install Aspire MCP server` option. This will install the Aspire MCP server on your machine, which is required for the agent to communicate with the AppHost.

> Note: If you are curious what each skill does, check out the Aspire documentation [Agent Skills](https://aspire.dev/get-started/aspire-skills/).

Once done, the agent will be registered and the skills will be installed. You should see a message similar to the following:

```text
🤖 Installed Aspire agent skills:                                                           
     Skills: aspire, aspire-deployment, aspire-init, aspire-monitoring, aspire-orchestration
     Locations: .agents/skills                                                              
✅ Configure VS Code to use the Aspire MCP server
✅ Agent environment configuration complete.
```

If you open Explorer, or Finder, you should see a new folder called `.agents` in your home directory. This is where the Aspire skills are installed.

### 4.2 Use the Aspire MCP agent

With AppHost running, wait for all of the resources to be healthy, then use the MCP commands from your agent/client:

```text
list_apphosts
select_apphost(appHostPath: "D:\\Projects\\aspire-workshop\\section-03\\lab-01\\CloudStore.AppHost")
```

For a list of all of the MCP commands, view the [Aspire MCP Server documentation](https://aspire.dev/get-started/aspire-mcp-server/).

#### Use the Aspire MCP agent Checkpoint

- MCP detects the CloudStore AppHost connection.
- The selected AppHost matches this lab workspace.

### 4.3 Run practical MCP health checks

Use MCP to inspect runtime state:

```text
list_resources
```

This should show all of the resources that are running in the AppHost, including the API, Web, PostgreSQL, Redis, and Azure Storage resources. The output should look similar to the following:

| Name | Type | State |
| ---- | ---- | ----- | 
| api-rebuilder | Executable | NotStarted |
| api | Project | Running |
| aspire-dashboard | Executable | Running |
| azureacd9b | AzureEnvironmentResource | — |
| blobs | AzureBlobStorageResource | Running |
| pgadmin | Container | Running |
| postgres | Container | Running |
| PostgreSQL (db) | PostgresDatabaseResource | Running |
| redis | Container | Running |
| storage | AzureStorageResource | Running |
| tables | AzureTableStorageResource | Running |
| web-installer | Executable | Finished |
| web | Executable | Running |

Everything is running except `api-rebuilder` (NotStarted, expected — it's a manual trigger) and `web-installer` (Finished, a one-shot setup task). Let me know if you want logs or details on any specific resource.

Some other commands to run.

```text
list_console_logs(resourceName: "api")
list_structured_logs(resourceName: "api")
list_traces
```

If a specific trace shows errors:

```text
list_trace_structured_logs(traceId: "<trace-id>")
```

#### Run practical MCP health checks Checkpoint

- Resources show expected running state (API, Web, PostgreSQL, Redis, Azure Storage).
- Logs/traces confirm successful dependency wiring and request flow.

### 4.4 Troubleshooting matrix

| Symptom                          | Likely cause                                         | How to verify                                     | Recovery                                                          |
| -------------------------------- | ---------------------------------------------------- | ------------------------------------------------- | ----------------------------------------------------------------- |
| PostgreSQL resource unhealthy    | Docker/image pull/start failure                      | Resource logs in dashboard or `list_console_logs` | Ensure Docker Desktop is running, retry `aspire start`            |
| Redis connection not used        | Missing API reference to Redis                       | API logs show no Redis connection string          | Ensure `.WithReference(redis)` exists on API resource and restart |
| Blob/table operations fail       | Azure Storage reference missing/misconfigured        | API exceptions mention storage auth/endpoint      | Ensure API references `storage` and restart AppHost               |
| Web calls wrong API URL          | Frontend still hardcoded to `https://localhost:7200` | Browser network tab / Web logs                    | Switch Web to runtime-injected API base URL and restart           |
| AppHost won’t start due to ports | Existing local containers/processes holding ports    | `docker ps` / startup errors                      | Stop conflicting containers/processes, rerun `aspire start`       |

### Migration note from `docker-compose.yml`

This lab intentionally migrates the previous local dependency model:

- `postgres` (`5432`)
- `redis` (`6379`)
- `azurite` (`10000-10002`)

from `docker-compose.yml` into Aspire resource orchestration so app processes and infrastructure are started, observed, and troubleshot from one control plane.

---

## Expected Outcome

- A single `aspire start` command launches CloudStore services and infrastructure dependencies.
- `CloudStore.Api` resolves PostgreSQL, Redis, and Azure Storage connection settings from Aspire-managed resources.
- `CloudStore.Web` calls the API using runtime configuration (no hardcoded localhost API URL).
- Aspire Dashboard shows healthy resources and successful cross-service requests.

---

## Troubleshooting

| Problem                             | Solution                                                    |
| ----------------------------------- | ----------------------------------------------------------- |
| "Cannot connect to AppHost"         | Ensure Docker Desktop is running and containers are healthy |
| API returns 503 Service Unavailable | Wait a few seconds for services to fully start              |
| Frontend shows blank page           | Check Logs tab in dashboard for errors                      |
| Port conflicts                      | Stop any services running on 5200, 4200, or 18888           |

If you get the message: `❌ Unable to stop one or more running Aspire AppHost instances. Please stop the application and try again.`, you can run the following commands to clean up any Aspire artifacts:

```bash
Remove-Item "$env:USERPROFILE\.aspire\cli\bch\*" -Force -ErrorAction SilentlyContinue
Remove-Item "$env:USERPROFILE\.aspire\cli\backchannels\*" -Force -ErrorAction SilentlyContinue
```

---

## Summary

In this lab, you migrated CloudStore from `docker-compose` to an Aspire AppHost and wired application configuration to resource references instead of hardcoded local endpoints.

You now have:

- A centralized orchestration model for API, Web, PostgreSQL, Redis, and Azure Storage.
- Runtime configuration injection between services through AppHost references.
- A repeatable workflow for validation and troubleshooting using Aspire Dashboard (and optionally Aspire MCP).

➡️ **Next:** [Section 4 Lab 1 – Deploying CloudStore to Azure Container Apps](../section-04/lab-01-deploying-to-azure-container-apps.md)
