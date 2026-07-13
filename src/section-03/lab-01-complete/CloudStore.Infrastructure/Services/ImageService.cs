namespace CloudStore.Infrastructure.Services;

public interface IImageService
{
    public bool IsValidImage(Stream imageStream);
}

public class ImageService: IImageService
{
    //  Defined image headers.
    private readonly byte[][] _imageHeaders = new byte[][]
    {
        new byte[]{ 0xFF, 0xD8 },                                       //  .jpg, .jpeg, .jpe, .jfif, .jif
        new byte[]{ 0x42, 0x4D},                                        //  .bmp
        new byte[]{ 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },   //  .png
        new byte[]{ 0x47, 0x49, 0x46 }                                  //  .gif
    };

    /// <summary>
    /// Validate that the stream is of an image file.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: The calling code is responsible for creating and disposing the image stream.
    /// Supported file types: .JPEG .BMP .GIF .PNG
    /// </remarks>
    /// <param name="imageStream">The stream of a picture file</param>
    /// <exception cref="Exception">Throws if the stream is of invalid image</exception>
    /// <returns></returns>
    public bool IsValidImage(Stream imageStream)
    {
        if (imageStream.Length <= 0) return false;
        var header = new byte[8]; // Change size if needed.
        imageStream.ReadExactly(header);

        var hasImageHeader = _imageHeaders.Count(magic =>
        {
            var i = 0;
            if(magic.Length > header.Length)
                return false;
            return magic.Count(b => b == header[ i++ ]) == magic.Length;
        }) > 0;

        return hasImageHeader;
    }
}