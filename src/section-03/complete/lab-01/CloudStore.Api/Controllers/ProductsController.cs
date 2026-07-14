using CloudStore.Infrastructure.Models;
using CloudStore.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace CloudStore.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController(IProductService productService, IStorageService storageService)
        : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetAll()
        {
            var products = await productService.GetAllProductsAsync();
            return Ok(products);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> GetById(int id)
        {
            var product = await productService.GetProductByIdAsync(id);
            if (product == null) return NotFound();
            return Ok(product);
        }

        [HttpPost]
        public async Task<ActionResult<Product>> Create([FromBody] CreateProductRequest request)
        {
            var product = new Product
            {
                Name = request.Name,
                Description = request.Description,
                Price = request.Price
            };
            var created = await productService.CreateProductAsync(product);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<Product>> Update(int id, [FromBody] CreateProductRequest request)
        {
            var product = await productService.GetProductByIdAsync(id);
            if (product == null) return NotFound();

            product.Name = request.Name;
            product.Description = request.Description;
            product.Price = request.Price;

            var updated = await productService.UpdateProductAsync(product);
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var success = await productService.DeleteProductAsync(id);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost("{id}/upload-image")]
        public async Task<IActionResult> UploadImage(int id, IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file provided");

            var product = await productService.GetProductByIdAsync(id);
            if (product == null) return NotFound();

            try
            {
                await using var stream = file.OpenReadStream();
                var blobName = await storageService.UploadProductImageAsync(id, stream, file.FileName);

                product.ImageUrl = blobName;
                await productService.UpdateProductAsync(product);

                return Ok(new { blobName = blobName, message = "Image uploaded successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }

    public class CreateProductRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }
}
