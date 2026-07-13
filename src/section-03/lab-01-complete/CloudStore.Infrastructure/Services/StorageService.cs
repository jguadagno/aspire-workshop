using Azure.Storage.Blobs;
using Azure.Data.Tables;

namespace CloudStore.Infrastructure.Services
{
    public interface IStorageService
    {
        Task<string> UploadProductImageAsync(int productId, Stream imageStream, string fileName);
        Task DeleteProductImageAsync(string blobName);
    }

    public class StorageService(BlobContainerClient blobContainer, TableClient queueTable, IImageService imageService) : IStorageService
    {
        private const string ContainerName = "product-images";
        private const string TableName = "ProductImageQueue";

        public async Task<string> UploadProductImageAsync(int productId, Stream imageStream, string fileName)
        {
            // Validate image
            try
            {
                if (!imageService.IsValidImage(imageStream))
                    throw new InvalidOperationException("Invalid image file");

                imageStream.Position = 0;
            }
            catch
            {
                throw new InvalidOperationException("Invalid image file");
            }

            // Upload blob
            imageStream.Position = 0;
            var blobName = $"products/{productId}/{Guid.NewGuid()}_{fileName}";
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

            await queueTable.CreateIfNotExistsAsync();
            await queueTable.AddEntityAsync(queueEntity);

            return blob.Uri.ToString();
        }

        public async Task DeleteProductImageAsync(string blobName)
        {
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
