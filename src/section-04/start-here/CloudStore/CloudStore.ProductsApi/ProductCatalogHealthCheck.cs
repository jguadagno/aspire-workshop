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