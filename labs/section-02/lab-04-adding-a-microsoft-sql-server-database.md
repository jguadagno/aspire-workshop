# Lab 4 – Adding a Microsoft SQL Server Database

**Estimated time:** 30 minutes  
**Difficulty:** Intermediate  
**Prerequisites:** Completed [Lab 3](./lab-03-adding-redis-caching.md)
**Required Software:** [Workshop Requirements](../../requirements.md)

---

## Objectives

By the end of this lab you will:

- ✅ Add a Microsoft SQL Server container along with [DbGate](https://www.dbgate.io/) for SQL management to the App Host
- ✅ Use EF Core with the Aspire Microsoft SQL Server integration in `ProductsApi`
- ✅ Seed initial data on creation of the database
- ✅ Browse the database with DbGate management UI from the dashboard
- ✅ Observe database spans in the Aspire dashboard traces

---

## Background

Aspire's Microsoft SQL Server hosting integration spins up a Microsoft SQL Server Docker container (and optionally a [DbGate](https://www.dbgate.io/) container) locally. The `Aspire.MicrosoftSqlServer.EntityFrameworkCore` client integration configures the connection string, retry policies, and OpenTelemetry instrumentation for EF Core automatically.

---

> Note: To preserve the previous lab, open Explorer or Finder and copy `lab-03-add-redis-cache` to `lab-04-add-database` before running the following commands.

## Step 1 – Add the Microsoft SQL Server Hosting Integration to AppHost

Starting from the folder `CloudStore` folder, run the following command to add the Microsoft SQL Server hosting integration package to the AppHost project:

```bash
dotnet add CloudStore.AppHost package Aspire.Hosting.SqlServer
```

Another way to add Microsoft SQL Server is to use the Aspire CLI:

```bash
aspire add sql-server
```

---

## Step 2 – Register Microsoft SQL Server in the App Host

Open `CloudStore.AppHost/AppHost.cs` and add the SQL Server resource:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Add a Redis container (Aspire pulls the image automatically)
var cache = builder.AddRedis("cache")
    .WithRedisInsight();

var sql = builder.AddSqlServer("cloudstore-sqlserver")
    .PublishAsConnectionString()    // <-- publish the connection string to the dashboard and inject into consuming projects
    .WithImageTag("2022-latest")    // <-- optional, defaults to latest 2022 image, if you are using a Mac, this is required since the default image is not compatible with Apple Silicon
    .WithLifetime(ContainerLifetime.Persistent)     // <-- optional, defaults to transient, but we want to persist the database across restarts
    .WithDataVolume("cloudstore-data");       // <-- optional, this allows you to persist the database data across restarts, otherwise it will be lost when the container is removed

var cloudStoreDb = sql.AddDatabase("CloudStore");

var apiService = builder.AddProject<Projects.CloudStore_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");
var productsApi = builder.AddProject<Projects.CloudStore_ProductsApi>("productsapi")
    .WithReference(cache)
    .WithReference(cloudStoreDb)    // <-- Inject the SQL Server connection string into the products API
    .WaitFor(cloudStoreDb)          // <-- wait for the database to be ready before starting the products API
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

> Note: The password is stored in the data volume. When using a data volume and if the password changes, it will not work until you delete the volume.
> Note: We removed the Health checks for the `productsapi` since it was "fake" and not actually checking the database.  The good news is that Aspire will automatically wait for the database to be ready before starting the `productsapi` since we added `.WaitFor(cloudStoreDb)` and the `Aspire.Microsoft.EntityFrameworkCore.SqlServer` we are adding in the next step adds a health check for the database connection automatically.

### Optionally, you can add the DbGate management UI to the dashboard

First, add the Community Toolkit SqlServer Extensions package.

```bash
dotnet add CloudStore.AppHost package CommunityToolkit.Aspire.Hosting.SqlServer.Extensions
```

Then, chain the `WithDbGate()` method on the SQL Server resource in `AppHost.cs`:

```csharp
var sql = builder.AddSqlServer("cloudstore-sqlserver")
    .PublishAsConnectionString() // <-- publish the connection string to the dashboard and inject into consuming projects
    .WithImageTag("2022-latest") // <-- optional, defaults to latest 2022 image, if you are using a Mac, this is required since the default image is not compatible with Apple Silicon
    .WithLifetime(ContainerLifetime
        .Persistent) // <-- optional, defaults to transient, but we want to persist the database across restarts
    .WithDataVolume(
        "cloudstore-data") // <-- optional, this allows you to persist the database data across restarts, otherwise it will be lost when the container is removed
    .WithDbGate();
```

---

## Step 3 – Add EF Core Packages to ProductsApi

```bash
# Aspire SqlServer EF Core integration (connection string, retries, telemetry)
dotnet add CloudStore.ProductsApi package Aspire.Microsoft.EntityFrameworkCore.SqlServer
```

---

## Step 4 – Create the Product Model and DbContext

> ***Note***: Both of these files can be copied from `src/section-02/lab-04-files/Data` if you want to skip typing them out.

Create `CloudStore.ProductsApi/Data/Product.cs`:

```csharp
namespace CloudStore.ProductsApi.Data;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Create `CloudStore.ProductsApi/Data/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace CloudStore.ProductsApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.Property(p => p.Category).HasMaxLength(100);
        });
    }
}
```

---

## Step 5 – Register the DbContext in ProductsApi

Open `CloudStore.ProductsApi/Program.cs` and update it:

```csharp
using CloudStore.ProductsApi.Data;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Wire up Redis as the output cache backing store.
// "cache" must match the name used in AddRedis("cache") in the AppHost.
builder.AddRedisOutputCache("cache");

// Register AppDbContext – Aspire reads the "CloudStore" connection string
// from environment variables injected by WithReference(cloudstoreDb) in AppHost
builder.AddSqlServerDbContext<AppDbContext>("CloudStore");

var app = builder.Build();

app.UseOutputCache();

app.MapDefaultEndpoints();

// GET /products – query from the database
app.MapGet("/products", [OutputCache(Duration = 60)] async (AppDbContext db) =>
{
    var products = await db.Products
        .OrderBy(p => p.Category)
        .ThenBy(p => p.Name)
        .ToListAsync();

    return Results.Ok(products);
});

// GET /products/{id}
app.MapGet("/products/{id:int}", async (int id, AppDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    return product is null ? Results.NotFound() : Results.Ok(product);
});

app.Run();
```

It is safe to remove the old static products code since we are now database-backed as well as the old `Product` record if you added that in the previous lab.

---

## Step 6 – Create the database, table, and seed data with a creation script

Now, there are two ways to initialize the database schema:

1. Run SQL creation scripts via AppHost (recommended for this lab)
2. Use EF Core migrations from the app code path

The first approach is recommended for this lab because it simplifies the workflow and ensures the database schema is always up to date with the provided scripts. The second approach is more traditional and may be preferred in production scenarios where you want more control over when and how migrations are applied.

We are going to use the first approach for this lab.

Aspire manages the database container (including the connection string), and also provides a way to run database scripts to initialize the schema. This is done with the `WithCreationScript` method on the database resource in AppHost.

Create a folder at the root of the solution called `scripts\database`. Inside that folder, create a new file called `create-database.sql` with the following content:

> Note: Make sure to replace `<REPLACE_ME>` with a password that meets SQL Server's password requirements (at least 8 characters, including uppercase, lowercase, number, and symbol) before running the app or the database creation will fail since we set the password in the SQL script and it's required for the SQL Server container to start properly.
> Note: These files can be copied from `src/section-02/lab-04-files/scripts` if you want to skip typing them out.

```sql
CREATE DATABASE CloudStore
    ON
    ( NAME = CloudStore_Data,
        FILENAME = '/var/opt/mssql/data/cloudstore.mdf',
        SIZE = 10,
        MAXSIZE = 50,
        FILEGROWTH = 5 )
    LOG ON
    ( NAME = CloudStore_Log,
        FILENAME = '/var/opt/mssql/data/cloudstore.ldf',
        SIZE = 5MB,
        MAXSIZE = 25MB,
        FILEGROWTH = 5MB ) ;
GO

USE master
GO
--- Replace <REPLACE_ME> with a real password
CREATE LOGIN CloudStoreAdmin WITH PASSWORD='<REPLACE_ME>'
GO

USE CloudStore
GO

CREATE USER CloudStoreAdmin FOR LOGIN CloudStoreAdmin;
ALTER ROLE db_datareader ADD MEMBER CloudStoreAdmin;
ALTER ROLE db_datawriter ADD MEMBER CloudStoreAdmin;
GO
```

Then create another file called `create-tables.sql` with the following content:

```sql
CREATE TABLE "Products"
(
    Id            int identity (1,1)                  NOT NULL
        CONSTRAINT "PK_Products" PRIMARY KEY,
    [Name]        varchar(200)                        NOT NULL,
    Price         decimal(18, 2)                      NOT NULL,
    Category      varchar(200)                        NOT NULL,
    StockQuantity int                                 NOT NULL,
    CreatedAt     datetimeoffset default getutcdate() NOT NULL
);  

```

Then, create another file called `seed-data.sql` with the following content:

```sql
INSERT INTO "Products" ("Name", "Price", "Category", "StockQuantity", "CreatedAt")
VALUES
    ('Cloud T-Shirt', 19.99, 'Apparel', 100, getutcdate()),
    ('Aspire Mug', 14.99, 'Accessories', 250, getutcdate()),
    ('Docker Sticker', 2.99, 'Accessories', 500, getutcdate()),
    ('Kubernetes Hoodie', 49.99, 'Apparel', 50, getutcdate()),
    ('NuGet Notebook', 9.99, 'Stationery', 150, getutcdate());
```

Now, update the `AddDatabase` call in `CloudStore.AppHost/AppHost.cs` to include these scripts:

> Note: We build a string with the files in the `scripts/database` folder and pass it to `WithCreationScript()` since the `WithCreationScript()` method accepts a single string. Aspire will run the provided SQL script when the database is created. In this example, we are creating the database `CloudStore`, creating the `Products` table and seeding it with initial data.

Paste over the existing `AddDatabase` code with the following:

```csharp
var path = builder.AppHostDirectory;
var sqlText = string.Concat(
    " ",
    File.ReadAllText(Path.Combine(path, @"../scripts/database/create-database.sql")),
    " ",
    File.ReadAllText(Path.Combine(path, @"../scripts/database/create-tables.sql")),
    " ",
    File.ReadAllText(Path.Combine(path, @"../scripts/database/seed-data.sql")));

var cloudStoreDb = sql.AddDatabase("CloudStore")
    .WithCreationScript(sqlText);    // run SQL to create tables and seed data
```

> Note: The `" "`, before and after each script is to ensure there is whitespace between the scripts when they are concatenated together, in case the last line of one script and the first line of the next script run together when combined.

---

## Step 7 – Run the Application

```bash
aspire run
```

The first time you run this, Aspire will create the database and run the creation script which will create the `Products` table and seed it with data. This may take a bit longer than usual since it's running the SQL scripts, but subsequent runs will be faster since the database is already created and the scripts will be skipped since we persisted the container `.WithLifetime(ContainerLifetime.Persistent)` and data `.WithDataVolume("cloudstore-data")`.

Watch the dashboard. You should now see:

| Name                   | Type      | Expected state              |
| ---------------------- | --------- | --------------------------- |
| `cache`                | Container | Running                     |
| `cloudstore-sqlserver` | Container | Running                     |
| `CloudStore`           | Database  | Running                     |
| `DbGate`               | Container | Running                     |
| `redisinsight`         | Container | Running                     |
| `apiservice`           | Project   | Running                     |
| `productsapi`          | Project   | Running (after DB is ready) |
| `webfrontend`          | Project   | Running                     |

> Note: The `DbGate` container may take a bit longer to start since it needs to initialize its own database. Aspire will automatically wait for the `cloudstore-sqlserver` container to be ready before starting `productsapi`, but it does not wait for `DbGate` since it's only used for database management and not required for the app to run.
> Note: The `productsapi` project might show as unhealthy or in a crash loop at first since it depends on the database being ready. Aspire will keep trying to start it and it should eventually become healthy once the database is up and accepting connections.

---

## Step 8 – Verify with DbGate

1. In the dashboard → **Resources**, find the `dbgate` container
2. Click its endpoint URL to open DbGate in your browser
3. The server connection is pre-configured — expand **Servers** in the left tree
4. Expand the `cloudstore-sqlserver` server
5. Navigate to `CloudStore`, then on the bottom pane **Tables, Views, Functions**, you should see **Tables**, expand that to see `Products`
6. Click → `Products` to see the table schema, and again to see the data — you should see the 5 seeded products from the `seed-data.sql` script

---

## Step 9 – Observe Database Traces

1. Open the web app → navigate to the **Products** page
2. In the dashboard → **Traces**, find the latest trace
3. You should see a name **webfrontend: GET /products** which has a spans for `webfrontend`, `productsapi`, and `cloudstore-sqlserver` (the database)
4. Click it, and a waterfall view will show the spans for each service, including the database query span which shows the actual SQL query that was executed
5. Click on the `SELECT [Products]` span to see the details, including the SQL query

---

## Expected Outcome

- Products are served from Microsoft SQL Server (visible in DbGate and in the Products page)
- Database spans appear in the Aspire dashboard traces with the actual SQL

---

## Troubleshooting

| Problem                        | Solution                                                       |
| ------------------------------ | -------------------------------------------------------------- |
| Products page shows empty list | Check `productsapi` logs — seeding may have failed silently    |
| `dbgate` not in dashboard      | Ensure `.WithDbGate()` is chained on `AddSqlServer` in AppHost |

---

## Summary

| Concept                           | Location                                                |
| --------------------------------- | ------------------------------------------------------- |
| Add Microsoft SQL Server + DbGate | `AppHost/AppHost.cs` → `AddSqlServer(...).WithDbGate()` |
| Create logical database           | `.AddDatabase("CloudStore")`                            |
| Inject DB connection string       | `WithReference(cloudstoreDb)` on consuming project      |
| Register EF Core DbContext        | `AddSqlServerDbContext<AppDbContext>("CloudStore")`     |
| Database spans in traces          | Automatic via Aspire's OpenTelemetry instrumentation    |

### Reference Links

- [Set up SQL Server in the AppHost](https://aspire.dev/integrations/databases/sql-server/sql-server-host/?aspire-lang=csharp)
- [Connecting to SQL Server from your app](https://aspire.dev/integrations/databases/sql-server/sql-server-connect/?aspire-lang=csharp)
- [SQL Server EF Core integrations overview](https://aspire.dev/integrations/databases/efcore/sql-server/sql-server-get-started/)
- [Use community extensions for SQL Server hosting](https://aspire.dev/integrations/databases/sql-server/sql-server-extensions/)
- [DbGate](https://www.dbgate.io/)

➡️ **Next:** [Lab 5 – Dashboard Deep Dive](./lab-05-mastering-the-aspire-dashboard.md)
