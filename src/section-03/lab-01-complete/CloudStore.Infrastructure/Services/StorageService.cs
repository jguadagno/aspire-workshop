using Azure.Storage.Blobs;
using Azure.Data.Tables;
using SixLabors.ImageSharp;

namespace CloudStore.Infrastructure.Services
{
    public interface IStorageService
    {
        Task<string> UploadProductImageAsync(int productId, Stream imageStream, string fileName);
        Task DeleteProductImageAsync(string blobName);
    }

    public class StorageService(BlobServiceClient blobServiceClient, TableServiceClient tableServiceClient) : IStorageService
    {
        private const string ContainerName = "product-images";
        private const string TableName = "ProductImageQueue";

        public async Task<string> UploadProductImageAsync(int productId, Stream imageStream, string fileName)
        {
            // Validate image
            try
            {
                imageStream.Position = 0;
                using var image = Image.Load(imageStream);
            }
            catch
            {
                throw new InvalidOperationException("Invalid image file");
            }

            // Upload blob
            imageStream.Position = 0;
            var blobName = $"products/{productId}/{Guid.NewGuid()}_{fileName}";
            var blobContainer = blobServiceClient.GetBlobContainerClient(ContainerName);
            await blobContainer.CreateIfNotExistsAsync();
            var blob = blobContainer.GetBlobClient(blobName);
            await blob.UploadAsync(imageStream, overwrite: true);

            // Add queue message for thumbnail processing
            var queueEntity = new ImageQueueEntity
            {
                PartitionKey = "imageprocessing",
                RowKey = Guid.NewGuid().ToString(),
                ProductId = productId,
                BlobName = blobName,
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            var queueTable = tableServiceClient.GetTableClient(TableName);
            await queueTable.CreateIfNotExistsAsync();
            await queueTable.AddEntityAsync(queueEntity);

            return blob.Uri.ToString();
        }

        public async Task DeleteProductImageAsync(string blobName)
        {
            var blobContainer = blobServiceClient.GetBlobContainerClient(ContainerName);
            var blob = blobContainer.GetBlobClient(blobName);
            await blob.DeleteAsync();
        }
    }

    // Queue entity for Azure Table Storage
    public class ImageQueueEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public Azure.ETag ETag { get; set; }

        public int ProductId { get; set; }
        public string BlobName { get; set; } = string.Empty;
        public string Status { get; set; } = "pending"; // pending, processing, completed, failed
        public DateTime CreatedAt { get; set; }
    }
}
