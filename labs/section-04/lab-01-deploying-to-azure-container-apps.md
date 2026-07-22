# Lab 1 – Deploying CloudStore to Azure Container Apps

**Estimated time:** 60 minutes  
**Difficulty:** Intermediate  
**Prerequisites:** Completed Section 2 labs, Docker Desktop running, active Azure subscription
**Required Software:** [Workshop Requirements](../../requirements.md)

---

## Objectives

By the end of this lab you will:

- ✅ Understand Aspire's pipeline-based deployment model (`aspire publish` vs `aspire deploy`)
- ✅ Add an Azure Container Apps compute environment to the AppHost
- ✅ Swap `AddSqlServer` and `AddRedis` for their Azure-native equivalents using `RunAsContainer`
- ✅ Configure Azure credentials and deployment settings
- ✅ Preview the deployment pipeline steps with `aspire deploy --list-steps`
- ✅ Deploy the full CloudStore solution to Azure Container Apps
- ✅ Verify the deployed storefront is live
- ✅ Tear down the deployed resources with `aspire destroy`

---

## Background

### How Aspire deployment works

Aspire's deployment model is **pipeline-based**. Deployment behavior doesn't live outside the AppHost — it comes from resources you add to the application model. When you add a compute environment resource (such as `AddAzureContainerAppEnvironment`), that resource contributes the steps needed to build images, push them to a registry, provision Azure infrastructure, and deploy each service.

Two commands enter the pipeline:

| Command          | Purpose                                                                                                                                                                                                      |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `aspire publish` | Emits target-specific artifacts (Bicep templates, Docker Compose files, manifests). Secrets remain as parameterized placeholders — a one-way handoff to another tool or a human step.                        |
| `aspire deploy`  | Resolves parameters, provisions infrastructure, builds and pushes images, and applies the deployment in one operation. Interactive on first run; can be fully automated via environment variables for CI/CD. |

### `RunAsContainer` vs the real Azure service

Aspire's Azure integration APIs separate **local development behavior** from **what gets deployed**. The `RunAsContainer` method configures a resource to run as a local Docker container during development, while `aspire publish`/`aspire deploy` targets the real Azure managed service:

| AppHost code                                | Local (`aspire start`)      | Azure (`aspire deploy`)                |
| ------------------------------------------- | --------------------------- | -------------------------------------- |
| `AddAzureRedis("cache").RunAsContainer()`   | Redis Docker container      | Azure Cache for Redis                  |
| `AddAzureSqlServer("sql").RunAsContainer()` | SQL Server Docker container | Azure SQL Server                       |
| `AddRedis("cache")`                         | Redis Docker container      | Azure Container App (Redis image)      |
| `AddSqlServer("sql")`                       | SQL Server Docker container | Azure Container App (SQL Server image) |

Switching to `AddAzureRedis` and `AddAzureSqlServer` means your local experience is **identical**, but production gets managed Azure services instead of containers running the database image.

---

## Getting Started

You can begin this lab with the starter application at `section-04/start-here`. If you want to preserve the original, copy it to a new folder (e.g. `section-04/lab-01`) before making changes.

All commands are run from inside the `CloudStore` folder (the folder that contains `CloudStore.sln`) unless a step states otherwise.

---

## Step 1 – Verify Prerequisites

### 1.1 Check required tools

Open a terminal and verify each tool:

```powershell
dotnet --version          # Should report 10.x.x
aspire --version          # Should report 13.x.x or later
az --version              # Should report 2.x.x
docker info               # Should succeed with no errors
```

If the Aspire CLI is missing or out of date, install or update it:

```powershell
dotnet tool update -g Aspire.Cli
```

If the Azure CLI is missing, download it from [https://learn.microsoft.com/cli/azure/install-azure-cli](https://learn.microsoft.com/cli/azure/install-azure-cli).

### 1.2 Sign in to Azure

```powershell
az login
```

This opens a browser window. Sign in with the account that has access to your Azure subscription. When it completes, the CLI prints the list of available subscriptions.

To confirm which subscription is active:

```powershell
az account show --query "{name:name, id:id, state:state}" -o table
```

To switch to a different subscription:

```powershell
az account set --subscription "<your-subscription-id>"
```

#### Step 1 Checkpoint

- `aspire --version` returns a value.
- `docker info` succeeds.
- `az account show` shows the correct subscription.

---

## Step 2 – Add Azure Hosting Integration Packages

The starter AppHost uses:

- `Aspire.Hosting.Redis` — runs a Redis container locally, **deploys Redis as an Azure Container App**
- `Aspire.Hosting.SqlServer` — runs SQL Server locally, **deploys SQL Server as an Azure Container App**

We will replace these with their Azure-native counterparts so that deployed resources become **Azure Cache for Redis** and **Azure SQL Server**. We also need the Azure Container Apps hosting integration that contributes the ACA deployment pipeline steps.

Run the following commands from the `CloudStore` folder:

```powershell
# Add the Azure Container Apps environment integration
aspire add azure-appcontainers

# Remove the plain Redis and SQL Server hosting packages
dotnet remove CloudStore.AppHost package Aspire.Hosting.Redis
dotnet remove CloudStore.AppHost package Aspire.Hosting.SqlServer

# Add Azure-native Redis and SQL Server hosting packages
dotnet add CloudStore.AppHost package Aspire.Hosting.Azure.Redis
dotnet add CloudStore.AppHost package Aspire.Hosting.Azure.Sql
```

> > Note: `aspire add azure-appcontainers` is interactive. When prompted, confirm the selection for `Aspire.Hosting.Azure.AppContainers`. It will add the package reference and update the `.csproj` automatically.
> > Note: We are using containers to deploy the web frontend and API services, but the database services are provisioned as managed Azure services. As of the creation of this lab, Azure App Servers are available with Aspire in Preview.

After running the commands, open `CloudStore.AppHost/CloudStore.AppHost.csproj` and verify the `<ItemGroup>` for packages looks like this:

```xml
<ItemGroup>
  <PackageReference Include="Aspire.Hosting.Azure.AppContainers" Version="13.4.6" />
  <PackageReference Include="Aspire.Hosting.Azure.Redis" Version="13.4.6" />
  <PackageReference Include="Aspire.Hosting.Azure.Sql" Version="13.4.6" />
  <PackageReference Include="CommunityToolkit.Aspire.Hosting.SqlServer.Extensions" Version="13.4.0" />
</ItemGroup>
```

> **Note:** Version numbers may differ. The important thing is that `Aspire.Hosting.Redis` and `Aspire.Hosting.SqlServer` have been replaced.

### Step 2 Checkpoint

- `dotnet build CloudStore.AppHost` succeeds (it will show compilation errors in Step 3 until the AppHost code is updated).

---

## Step 3 – Update the AppHost

Open `CloudStore.AppHost/AppHost.cs`. We need to make three changes:

1. Add the Azure Container Apps environment.
2. Replace `AddRedis` with `AddAzureRedis(...).RunAsContainer(...)`.
3. Replace `AddSqlServer` with `AddAzureSqlServer(...).RunAsContainer(...)`.

Replace the entire contents of `AppHost.cs` with the following:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// --- Deployment target -----------------------------------------------------------
// AddAzureContainerAppEnvironment registers the ACA compute environment.
// All project resources attach to it automatically when you run `aspire deploy`.
builder.AddAzureContainerAppEnvironment("env");

// --- Infrastructure resources ----------------------------------------------------
// AddAzureRedis:   local dev → Redis Docker container  |  deploy → Azure Cache for Redis
// RunAsContainer:  keeps local dev behavior identical to before
var cache = builder.AddAzureManagedRedis("cache")
    .RunAsContainer(c => c
        .WithClearCommand()
        .WithRedisInsight());

// AddAzureSqlServer:  local dev → SQL Server Docker container  |  deploy → Azure SQL Server
// RunAsContainer:     keeps local dev behavior identical to before
// PublishAsConnectionString() is removed — Azure SQL is provisioned via Bicep, not a plain conn string
var sql = builder.AddAzureSqlServer("cloudstore-sqlserver")
    .RunAsContainer(c => c
        .WithImageTag("2022-latest")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume("cloudstore-data")
        .WithDbGate());

var cloudStoreDb = sql.AddDatabase("CloudStore");

// --- Application services --------------------------------------------------------
var apiService = builder.AddProject<Projects.CloudStore_ApiService>("apiservice")
    .WithFriendlyUrls("CloudStore API")
    .WithHttpHealthCheck("/health");

var productsApi = builder.AddProject<Projects.CloudStore_ProductsApi>("productsapi")
    .WithFriendlyUrls("Products API")
    .WithReference(cache)
    .WithReference(cloudStoreDb)
    .WithHttpHealthCheck("/health")
    .WaitFor(cloudStoreDb)
    .WaitFor(cache);

builder.AddProject<Projects.CloudStore_Web>("webfrontend")
    .WithFriendlyUrls("CloudStore Web")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WithReference(productsApi)
    .WithReference(cache)
    .WaitFor(apiService)
    .WaitFor(productsApi)
    .WaitFor(cache);

// --- HealthChecksUI (local dev only) ---------------------------------------------
// HealthChecksUI uses the Aspire container network for health probe URLs.
// We restrict it to run mode so it does not attempt to deploy to ACA.
if (builder.ExecutionContext.IsRunMode)
{
    builder.AddHealthChecksUI("healthchecksui")
        .WithReference(apiService)
        .WithReference(productsApi)
        .WithFriendlyUrls("Health Checks UI Dashboard")
        .WithHostPort(7230);
}

builder.Build().Run();
```

### What changed and why

| Change                                           | Reason                                                                                                                                                                                                                                                                                                             |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `builder.AddAzureContainerAppEnvironment("env")` | Registers the ACA compute environment. Without this, `aspire deploy` has nothing to do.                                                                                                                                                                                                                            |
| `AddAzureRedis(...).RunAsContainer(...)`         | Local dev keeps the Redis container; `aspire deploy` provisions Azure Cache for Redis and wires up managed identity.                                                                                                                                                                                               |
| `AddAzureSqlServer(...).RunAsContainer(...)`     | Local dev keeps the SQL container; `aspire deploy` provisions Azure SQL Server.                                                                                                                                                                                                                                    |
| Removed `.PublishAsConnectionString()`           | That was a hint for plain connection string injection. Azure SQL uses managed identity — no connection string to publish.                                                                                                                                                                                          |
| `healthChecksUI` wrapped in `IsRunMode`          | The HealthChecksUI resource wires up probe URLs using the Aspire container network identifier. Restricting it to run mode keeps local dev working without deploying an unnecessary container to ACA.                                                                                                               |
| Removed `.WithCreationScript(...)`               | The `AddAzureSqlServer(...)` does not support the database script creation, which we only want to do in Development. The SQL Server container is now provisioned by Azure SQL Server, so the creation script is no longer needed. The seeding was moved from the previous project to the EF Core Context creation. |

Verify the project builds:

```powershell
dotnet build CloudStore.AppHost
```

#### Step 3 Checkpoint

- `dotnet build CloudStore.AppHost` succeeds with no errors.
- Run `aspire start` briefly to confirm local dev still works (SQL container, Redis container, and all three services start). Press Ctrl+C to stop before continuing.

---

## Step 4 – Configure Azure Deployment Settings

Aspire needs three values to deploy to Azure. Currently, Aspire reads them from the environment. If you don't set them, Aspire will prompt you for them on the first `aspire deploy`.

> **Note**: Optional for this lab, but recommended for CI/CD pipelines. You can also set these values in a `.env` file or via your CI/CD pipeline's secret management.

| Setting                 | What it controls                                                               |
| ----------------------- | ------------------------------------------------------------------------------ |
| `Azure__SubscriptionId` | The Azure subscription that owns the deployed resources                        |
| `Azure__Location`       | The Azure region where resources are provisioned (e.g. `eastus`, `westeurope`) |
| `Azure__ResourceGroup`  | The resource group to create (or reuse)                                        |

Replace `<your-subscription-id>` with the subscription ID shown by `az account show`. Replace `eastus` and `cloudstore-rg` with your preferred region and resource group name.

> **Tip:** You can use any region that supports Azure Container Apps and Azure SQL Server. Run `az account list-locations -o table` to see available regions.
> **Note:** If the resource group does not exist, Aspire creates it automatically. If it already exists and you want Aspire to reuse it without creating a new one, also set `Azure__AllowResourceGroupCreation` to `false`.

### Step 4 Checkpoint

Verify the secrets were saved:

```powershell
Get-ChildItem Env: | Where-Object { $_.Name -match "Azure__" }
```

You should see `Azure__SubscriptionId`, `Azure__Location`, and `Azure__ResourceGroup` in the output.

---

## Step 5 – Preview the Deployment Pipeline

Before deploying, preview the steps Aspire would execute:

```powershell
aspire deploy --list-steps
```

Expected output (resource names may vary):

```text
Pipeline steps:
  deploy-prereq
  validate-azure-login
  build-prereq
  create-provisioning-context
  provision-env
  login-to-acr-env
  build-webfrontend
  build-apiservice
  build-productsapi
  push-webfrontend
  push-apiservice
  push-productsapi
  provision-webfrontend-containerapp
  provision-apiservice-containerapp
  provision-productsapi-containerapp
  provision-azure-bicep-resources
  print-webfrontend-summary
  print-apiservice-summary
  print-productsapi-summary
  print-dashboard-url-env
  deploy
```

This confirms that Aspire has detected the ACA environment and will:

1. Validate your Azure login and resolve the subscription and resource group.
2. Provision the ACA environment (Azure Container Registry, managed environment, Log Analytics workspace).
3. Provision Azure SQL Server and Azure Cache for Redis via Bicep.
4. Build Docker images for each .NET project.
5. Push the images to the provisioned ACR.
6. Deploy each image as an Azure Container App.

> **Note:** If you see only `deploy-prereq` and `deploy` with a total of 2 steps, the ACA environment was not registered correctly. Double-check that `builder.AddAzureContainerAppEnvironment("env")` is present in `AppHost.cs` and that the `Aspire.Hosting.Azure.AppContainers` package is referenced.

---

## Step 6 – Deploy

Run the deployment from the `CloudStore` folder:

```powershell
aspire deploy
```

On the **first run** Aspire will:

1. Validate your Azure CLI credentials.
2. Prompt you to confirm the subscription, location, and resource group if they were not set via secrets (you set them in Step 4, so this should be skipped).
3. Provision the ACA environment. This creates an Azure Container Registry, an ACA managed environment, and a Log Analytics workspace — this step takes 2–5 minutes.
4. Build all three project images in parallel.
5. Push the images to ACR.
6. Deploy the Container Apps and provision Azure SQL and Redis via Bicep.

Expected terminal output (abbreviated):

```text
09:19:34 (pipeline execution) → Starting pipeline execution...
09:19:34 (deploy-prereq) ✓ deploy-prereq completed successfully
09:19:36 (validate-azure-login) ✓ Azure CLI authentication validated successfully
09:19:39 (create-provisioning-context) ✓ create-provisioning-context completed successfully
09:19:39 (provision-env) → Deploying env
09:21:30 (provision-env) ✓ provision-env completed successfully
09:21:33 (login-to-acr-env) ✓ Successfully logged in to ACR
09:24:00 (build-webfrontend)  ✓ build-webfrontend completed successfully
09:24:00 (build-apiservice)   ✓ build-apiservice completed successfully
09:24:00 (build-productsapi)  ✓ build-productsapi completed successfully
09:25:15 (push-webfrontend)   ✓ Successfully pushed webfrontend to ACR
09:25:45 (push-apiservice)    ✓ Successfully pushed apiservice to ACR
09:25:50 (push-productsapi)   ✓ Successfully pushed productsapi to ACR
09:26:30 (provision-webfrontend-containerapp)  ✓ Successfully provisioned webfrontend-containerapp
09:26:35 (provision-apiservice-containerapp)   ✓ Successfully provisioned apiservice-containerapp
09:26:40 (provision-productsapi-containerapp)  ✓ Successfully provisioned productsapi-containerapp
09:26:45 (provision-azure-bicep-resources)     ✓ provision-azure-bicep-resources completed successfully
09:26:46 (print-webfrontend-summary) i [INF] Successfully deployed webfrontend to
  https://webfrontend.nicesea-xxxxxxxx.eastus.azurecontainerapps.io
09:26:46 (deploy) ✓ deploy completed successfully
09:26:46 (pipeline execution) ✓ Completed successfully
------------------------------------------------------------
✓ 43/43 steps succeeded • Total time: ~7 minutes
Steps Summary
...
✓ PIPELINE SUCCEEDED

  ☁️ Target: Azure
  📦 Resource Group: <your_resource_group>
  📜 Deployments: Azure Portal
  🔑 Subscription: <your_subscription_id>
  🌐 Location: <your_region>
  apiservice: No public endpoints (Azure Portal)
  productsapi: No public endpoints (Azure Portal)
  webfrontend: https://webfrontend.<your_id>.<your_region>.azurecontainerapps.io (Azure Portal)
  📊 Dashboard: https://aspire-dashboard.ext.<your_id>.<your_region>.azurecontainerapps.io
------------------------------------------------------------
```

> **Tip:** The first deployment takes longer because the ACA environment, ACR, Azure SQL Server, and Azure Cache for Redis all need to be provisioned. Subsequent deployments reuse the provisioned infrastructure and are significantly faster.

**Troubleshooting:** If a step fails, the error message includes the pipeline step name. Common issues:

- `validate-azure-login` fails → run `az login` again.
- `provision-env` times out → Azure resource provisioning can take longer in some regions. Re-run `aspire deploy`; it resumes from where it left off using deployment state caching.
- Image build fails → ensure Docker Desktop is running and you have sufficient disk space.

Helpful Paths

| Path | Description |
| ---- | ----------- |
| `$env:USERPROFILE/.aspire/deployments` | Contains the deployment state cache. Aspire uses this to skip unchanged steps on subsequent deployments. |
| `$env:USERPROFILE/.aspire/logs` | Contains the Aspire logs. |

---

## Step 7 – Verify the Deployment

### 7.1 Open the storefront

In the deployment output, find the line that starts with `print-webfrontend-summary`:

```text
Successfully deployed webfrontend to https://webfrontend.<id>.<region>.azurecontainerapps.io
```

Open that URL in your browser. Newer versions of Aspire will print that URL to the console. The CloudStore storefront should load and display products from the database. However, we did not seed the database (in production) in this lab, so the products page will be empty. The important thing is that the web frontend is live and connected to the API services.

### 7.2 Check the resources in the Azure portal

1. Open [https://portal.azure.com](https://portal.azure.com).
2. Navigate to **Resource groups** → `cloudstore-rg` (or whatever name you used).
3. You should see:
   - An **Azure Container Apps environment** (`env-...`)
   - An **Azure Container Registry** (`envcr...`)
   - Three **Container Apps** — `webfrontend`, `apiservice`, `productsapi`
   - An **Azure SQL Server** (`cloudstore-sqlserver-...`)
   - An **Azure Cache for Redis** (`cache-...`)
   - A **Log Analytics workspace**

### 7.3 Inspect logs with the Aspire MCP server (optional)

If you have the Aspire MCP server configured in your editor, you can inspect the live resources, logs, and traces of the running application while it is orchestrated locally.

> **Note:** The Aspire MCP server connects to a locally running Aspire instance. For deployed resources, use the Azure portal's Log Analytics workspace or Application Insights queries.

#### Step 7 Checkpoint

- The CloudStore web frontend loads at the Azure Container Apps URL.
- The products page shows data, confirming `productsapi` connected to Azure SQL Server.
- The Azure portal shows all expected resources in `cloudstore-rg`.

---

## Step 8 – Re-deploy After a Code Change (Optional)

To verify that incremental deployments work, make a small change (for example, edit the page title in `CloudStore.Web`), then re-run:

```powershell
aspire deploy
```

Aspire uses deployment state caching to skip steps that have not changed. Only the modified service's image is rebuilt and pushed; the Azure infrastructure steps are skipped entirely, making the second deployment much faster.

---

## Step 9 – Clean Up Resources

> **Caution:** `aspire destroy` deletes the entire Azure resource group and **everything** in it — the ACA environment, ACR, all Container Apps, Azure SQL Server, and Azure Cache for Redis. This action **cannot be undone**. Only proceed if you are finished with the deployment.

When you are ready to remove the deployed resources to avoid ongoing Azure charges:

```powershell
aspire destroy
```

Aspire discovers the deployment state, shows you what will be removed, and prompts for confirmation before proceeding. Type `y` to confirm.

Expected output:

```text
Discovering deployments...
The following resources will be destroyed:
  - Resource group: cloudstore-rg (and all resources within it)

Are you sure you want to destroy these resources? (y/N): y

------------------------------------------------------------
✅ 9/9 steps succeeded • Total time: 1m 32s

Steps Summary:
                Step timeline:                      0s                      1m 32s
                                                    │───────┬──────┬─────┬───────│
      4.39ms  ✓ azure-prepare-resources             │╴                           │
      9.27ms  ✓   prepare-azure-container-apps-env  │╴                           │
      3.88ms  ✓     before-start                    │╴                           │
      2.43ms  ✓ validate-compute-environments       │╴                           │
      1.83ms  ✓ validate-azure-container-apps       │╴                           │
      1m 31s  ✓ pipeline-execution                  │╶──────────────────────────╴│
      4.57ms  ✓ destroy-prereq                      │╴                           │
      1m 31s  ✓   destroy-azure-azureacd9b          │╶──────────────────────────╴│
      4.30ms  ✓     destroy                         │                           ╴│

✅ Pipeline succeeded

  🗑️ Resource Group: <your_resource_group>
  🔑 Subscription: <your_subscription_id>
  ⏳ Status: Deletion in progress. Monitor here
------------------------------------------------------------
```

---

## Expected Outcome

- `aspire deploy` provisions Azure infrastructure and deploys CloudStore services without manual Bicep editing.
- The `webfrontend` endpoint is reachable in Azure Container Apps.
- Azure portal shows ACA environment, container apps, ACR, Azure SQL Server, Azure Cache for Redis, and Log Analytics resources.
- `aspire destroy` removes the deployment resources when you are done.

---

## Troubleshooting

| Problem                                                                     | Likely cause                                                      | Fix                                                                                                                    |
| --------------------------------------------------------------------------- | ----------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `aspire deploy --list-steps` shows only 2 steps (`deploy-prereq`, `deploy`) | ACA environment not registered in AppHost                         | Ensure `builder.AddAzureContainerAppEnvironment("env")` exists and `Aspire.Hosting.Azure.AppContainers` is referenced. |
| `validate-azure-login` fails                                                | Azure CLI session expired or wrong tenant/subscription            | Re-run `az login`, then verify `az account show` and set the intended subscription with `az account set`.              |
| Build or push image steps fail                                              | Docker daemon not running or local disk full                      | Start Docker Desktop, free disk space, and rerun `aspire deploy`.                                                      |
| SQL/Redis provisioning fails                                                | Regional availability/quota or transient Azure provisioning error | Try another supported region and rerun `aspire deploy`; pipeline state allows resume behavior.                         |
| Web frontend deploys, but no products appear                                | Database not seeded in production deployment path                 | This lab validates deployment connectivity; seed data separately if required for demo content.                         |
| `aspire destroy` leaves resource group in deleting state                    | Azure asynchronous deletion still in progress                     | Wait and monitor in Azure Portal; deletion can take several minutes for ACA/SQL dependencies.                          |

---

## Summary

In this lab you:

1. Added `AddAzureContainerAppEnvironment("env")` to register Azure Container Apps as the deployment target.
2. Replaced `AddRedis`/`AddSqlServer` with `AddAzureRedis`/`AddAzureSqlServer` combined with `RunAsContainer` — keeping local dev identical while pointing production at managed Azure services.
3. Configured Azure deployment settings using `aspire secret set`.
4. Previewed the pipeline with `aspire deploy --list-steps`.
5. Deployed the full CloudStore solution with a single `aspire deploy` command.
6. Verified the live application in Azure.
7. Cleaned up with `aspire destroy`.

The key insight is that Aspire's `AddAzure*` APIs decouple **how a resource runs locally** from **how it is provisioned in Azure**, with no changes required to the application service code itself.

---

## Further Reading

- [Aspire deployment overview](https://aspire.dev/deployment/)
- [Deploy with Aspire](https://aspire.dev/deployment/deploy-with-aspire/)
- [Deploy to Azure](https://aspire.dev/deployment/azure/)
- [Azure integrations overview](https://aspire.dev/integrations/cloud/azure/overview/)
- [Customize Azure resources](https://aspire.dev/integrations/cloud/azure/customize-resources/)
- [Azure security best practices](https://aspire.dev/deployment/azure/azure-security-best-practices/)
- [`aspire deploy` CLI reference](https://aspire.dev/reference/cli/commands/aspire-deploy/)
- [`aspire destroy` CLI reference](https://aspire.dev/reference/cli/commands/aspire-destroy/)
