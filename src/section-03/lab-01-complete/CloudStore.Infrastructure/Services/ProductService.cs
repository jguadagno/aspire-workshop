using CloudStore.Infrastructure.Data;
using CloudStore.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace CloudStore.Infrastructure.Services
{
    public interface IProductService
    {
        Task<IEnumerable<Product>> GetAllProductsAsync();
        Task<Product?> GetProductByIdAsync(int id);
        Task<Product> CreateProductAsync(Product product);
        Task<Product> UpdateProductAsync(Product product);
        Task<bool> DeleteProductAsync(int id);
        Task InvalidateProductCacheAsync();
    }

    public class ProductService(CloudStoreDbContext dbContext, IConnectionMultiplexer? redis = null)
        : IProductService
    {
        private const string CacheKey = "products:all";

        public async Task<IEnumerable<Product>> GetAllProductsAsync()
        {
            if (redis != null)
            {
                var db = redis.GetDatabase();
                var cached = db.StringGet(CacheKey);
                if (cached.HasValue)
                {
                    return System.Text.Json.JsonSerializer.Deserialize<List<Product>>(cached.ToString())
                        ?? new List<Product>();
                }
            }

            var products = await dbContext.Products.ToListAsync();

            if (redis != null)
            {
                var db = redis.GetDatabase();
                var json = System.Text.Json.JsonSerializer.Serialize(products);
                await db.StringSetAsync(CacheKey, json, TimeSpan.FromMinutes(10));
            }

            return products;
        }

        public async Task<Product?> GetProductByIdAsync(int id)
        {
            return await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<Product> CreateProductAsync(Product product)
        {
            product.CreatedAt = DateTime.UtcNow;
            product.UpdatedAt = DateTime.UtcNow;
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();
            await InvalidateProductCacheAsync();
            return product;
        }

        public async Task<Product> UpdateProductAsync(Product product)
        {
            product.UpdatedAt = DateTime.UtcNow;
            dbContext.Products.Update(product);
            await dbContext.SaveChangesAsync();
            await InvalidateProductCacheAsync();
            return product;
        }

        public async Task<bool> DeleteProductAsync(int id)
        {
            var product = await GetProductByIdAsync(id);
            if (product == null) return false;

            dbContext.Products.Remove(product);
            await dbContext.SaveChangesAsync();
            await InvalidateProductCacheAsync();
            return true;
        }

        public async Task InvalidateProductCacheAsync()
        {
            if (redis != null)
            {
                var db = redis.GetDatabase();
                await db.KeyDeleteAsync(CacheKey);
            }
        }
    }
}
