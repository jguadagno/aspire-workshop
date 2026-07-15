namespace CloudStore.Web;

public class ProductsApiClient(HttpClient httpClient)
{
    public async Task<Product[]> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<Product[]>(
            "/products", cancellationToken) ?? [];
    }
}

public record Product(int Id, string Name, decimal Price, string Category);