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

    /// <summary>
    /// Wires up EF Core's UseSeeding/UseAsyncSeeding hooks so the sample product
    /// catalog is populated automatically — but only when running in the
    /// Development environment. The seeding delegates run whenever
    /// Database.EnsureCreated() is invoked.
    /// </summary>
    public static void UseDevelopmentSeeding(DbContextOptionsBuilder optionsBuilder, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        optionsBuilder.UseSeeding((context, _) =>
        {
            SeedProducts((AppDbContext)context);
        });

        optionsBuilder.UseAsyncSeeding(async (context, _, cancellationToken) =>
        {
            await SeedProductsAsync((AppDbContext)context, cancellationToken);
        });
    }

    private static void SeedProducts(AppDbContext context)
    {
        if (context.Products.Any())
        {
            return;
        }

        context.Products.AddRange(GetSeedProducts());
        context.SaveChanges();
    }

    private static async Task SeedProductsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        context.Products.AddRange(GetSeedProducts());
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<Product> GetSeedProducts() =>
        new[]
        {
            new Product { Name = "Cloud T-Shirt", Price = 19.99m, Category = "Apparel", StockQuantity = 100 },
            new Product { Name = "Aspire Mug", Price = 14.99m, Category = "Accessories", StockQuantity = 250 },
            new Product { Name = "Docker Sticker", Price = 2.99m, Category = "Accessories", StockQuantity = 500 },
            new Product { Name = "Kubernetes Hoodie", Price = 49.99m, Category = "Apparel", StockQuantity = 50 },
            new Product { Name = "NuGet Notebook", Price = 9.99m, Category = "Stationery", StockQuantity = 150 },
        };
}