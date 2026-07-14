using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using CloudStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Blobs;
using Azure.Data.Tables;
using Moq;
using Microsoft.AspNetCore.Hosting;
using StackExchange.Redis;

namespace CloudStore.Api.Tests
{
    public class ProductsControllerTests : IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public ProductsControllerTests()
        {
            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.ConfigureTestServices(services =>
                    {
                        // Add in-memory database
                        services.AddDbContext<CloudStoreDbContext>(options =>
                        {
                            options.UseInMemoryDatabase("TestDb");
                        });

                        // Mock BlobContainerClient
                        var mockBlobContainer = new Mock<BlobContainerClient>();
                        var mockBlobClient = new Mock<BlobClient>();
                        mockBlobContainer.Setup(b => b.GetBlobClient(It.IsAny<string>())).Returns(mockBlobClient.Object);
                        services.AddSingleton(mockBlobContainer.Object);

                        // Mock TableClient
                        var mockTableClient = new Mock<TableClient>();
                        services.AddSingleton(mockTableClient.Object);

                        // Mock Redis
                        var mockRedis = new Mock<IConnectionMultiplexer>();
                        var mockDatabase = new Mock<IDatabase>();
                        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);
                        
                        // Setup mockDatabase to return a value for CacheKey to cover more branches
                        mockDatabase.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                            .Returns(RedisValue.Null); // First time it's null, triggers DB load and cache set
                        
                        services.AddSingleton(mockRedis.Object);
                    });
                });
            _client = _factory.CreateClient();

            // Seed the in-memory database
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CloudStoreDbContext>();
                db.Database.EnsureDeleted();
                db.Database.EnsureCreated(); // This will also apply HasData from OnModelCreating
            }
        }

        public void Dispose()
        {
            _client.Dispose();
            _factory.Dispose();
        }

        [Fact]
        public async Task GetAllProducts_ReturnsOkStatus()
        {
            // Arrange
            var endpoint = "/api/products";

            // Act
            var response = await _client.GetAsync(endpoint);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetAllProducts_WithCacheHit_ReturnsOkStatus()
        {
            // Arrange
            // We need to re-configure the factory to mock a cache hit
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    var mockRedis = new Mock<IConnectionMultiplexer>();
                    var mockDatabase = new Mock<IDatabase>();
                    mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);
                    
                    var products = new List<Infrastructure.Models.Product>
                    {
                        new Infrastructure.Models.Product { Id = 1, Name = "Cached Product", Price = 10 }
                    };
                    var json = System.Text.Json.JsonSerializer.Serialize(products);
                    
                    mockDatabase.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                        .Returns(json);
                    
                    services.AddSingleton(mockRedis.Object);
                });
            });
            var client = factory.CreateClient();
            var endpoint = "/api/products";

            // Act
            var response = await client.GetAsync(endpoint);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetProductById_WithValidId_ReturnsOkStatus()
        {
            // Arrange
            var endpoint = "/api/products/1";

            // Act
            var response = await _client.GetAsync(endpoint);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetProductById_WithInvalidId_ReturnsNotFoundStatus()
        {
            // Arrange
            var endpoint = "/api/products/9999";

            // Act
            var response = await _client.GetAsync(endpoint);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task CreateProduct_WithValidRequest_ReturnsCreatedStatus()
        {
            // Arrange
            var endpoint = "/api/products";
            var content = new StringContent(
                "{\"name\":\"Test Product\",\"description\":\"Test\",\"price\":99.99}",
                System.Text.Encoding.UTF8,
                "application/json"
            );

            // Act
            var response = await _client.PostAsync(endpoint, content);

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task UpdateProduct_WithValidId_ReturnsOkStatus()
        {
            // Arrange
            var id = 1;
            var endpoint = $"/api/products/{id}";
            var content = new StringContent(
                "{\"name\":\"Updated Product\",\"description\":\"Updated\",\"price\":199.99}",
                System.Text.Encoding.UTF8,
                "application/json"
            );

            // Act
            var response = await _client.PutAsync(endpoint, content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task UpdateProduct_WithInvalidId_ReturnsNotFoundStatus()
        {
            // Arrange
            var endpoint = "/api/products/999";
            var content = new StringContent(
                "{\"name\":\"Updated Product\",\"description\":\"Updated\",\"price\":199.99}",
                System.Text.Encoding.UTF8,
                "application/json"
            );

            // Act
            var response = await _client.PutAsync(endpoint, content);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task DeleteProduct_WithValidId_ReturnsNoContentStatus()
        {
            // Arrange
            var id = 2;
            var endpoint = $"/api/products/{id}";

            // Act
            var response = await _client.DeleteAsync(endpoint);

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task DeleteProduct_WithInvalidId_ReturnsNotFoundStatus()
        {
            // Arrange
            var endpoint = "/api/products/999";

            // Act
            var response = await _client.DeleteAsync(endpoint);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task UploadImage_WithValidFile_ReturnsOkStatus()
        {
            // Arrange
            var id = 1;
            var endpoint = $"/api/products/{id}/upload-image";
            
            // Create a dummy image file
            var fileContent = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x60, 0x00, 0x60, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09, 0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12, 0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20, 0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29, 0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32, 0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x00, 0xFF, 0xD9 };
            var content = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(fileContent);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(imageContent, "file", "test.jpg");

            // Act
            var response = await _client.PostAsync(endpoint, content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task UploadImage_WithInvalidFile_ReturnsBadRequest()
        {
            // Arrange
            var id = 1;
            var endpoint = $"/api/products/{id}/upload-image";
            var content = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(new byte[] { 0, 1, 2, 3 });
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(imageContent, "file", "test.jpg");

            // Act
            var response = await _client.PostAsync(endpoint, content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task UploadImage_WithNoFile_ReturnsBadRequest()
        {
            // Arrange
            var id = 1;
            var endpoint = $"/api/products/{id}/upload-image";
            var content = new MultipartFormDataContent();

            // Act
            var response = await _client.PostAsync(endpoint, content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
