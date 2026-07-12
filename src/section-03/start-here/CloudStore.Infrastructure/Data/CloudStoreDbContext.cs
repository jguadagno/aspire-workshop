using CloudStore.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace CloudStore.Infrastructure.Data
{
    public class CloudStoreDbContext(DbContextOptions<CloudStoreDbContext> options) : DbContext(options)
    {
        public DbSet<Product> Products { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Seed sample data
            modelBuilder.Entity<Product>().HasData(
                new Product
                {
                    Id = 1,
                    Name = "Laptop",
                    Description = "High-performance laptop for developers",
                    Price = 1299.99m,
                    ImageUrl = "https://placehold.co/400x300?text=Laptop",
                    CreatedAt = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc)
                },
                new Product
                {
                    Id = 2,
                    Name = "Mechanical Keyboard",
                    Description = "Cherry MX switches, RGB backlit",
                    Price = 149.99m,
                    ImageUrl = "https://placehold.co/400x300?text=Keyboard",
                    CreatedAt = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc)
                },
                new Product
                {
                    Id = 3,
                    Name = "4K Monitor",
                    Description = "Dell UltraSharp 27-inch 4K monitor",
                    Price = 599.99m,
                    ImageUrl = "https://placehold.co/400x300?text=Monitor",
                    CreatedAt = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc)
                }
            );
        }
    }
}
