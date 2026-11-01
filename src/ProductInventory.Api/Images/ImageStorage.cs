namespace ProductInventory.Api.Images;

public class ImageStorageOptions
{
    public const string SectionName = "ImageStorage";

    /// <summary>Directory for product images. Relative paths are resolved against the content root.</summary>
    public string Path { get; set; } = "data/images";

    public long MaxBytes { get; set; } = 5 * 1024 * 1024;
}

public record StoredImage(string RelativePath, string ContentType);

public interface IImageStorage
{
    Task<StoredImage> SaveAsync(Guid productId, Stream content, CancellationToken ct);
    Stream? OpenRead(string relativePath);
    void Delete(string? relativePath);
}

public class ImageValidationException(string message) : Exception(message);

public class DiskImageStorage : IImageStorage
{
    private readonly string _root;
    private readonly long _maxBytes;

    public DiskImageStorage(Microsoft.Extensions.Options.IOptions<ImageStorageOptions> options, IHostEnvironment env)
    {
        var path = options.Value.Path;
        _root = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(env.ContentRootPath, path));
        _maxBytes = options.Value.MaxBytes;
        Directory.CreateDirectory(_root);
    }

    public long MaxBytes => _maxBytes;

    public async Task<StoredImage> SaveAsync(Guid productId, Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > _maxBytes)
            {
                throw new ImageValidationException($"Image exceeds the maximum size of {_maxBytes / 1024 / 1024} MB.");
            }
            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        var (extension, contentType) = DetectFormat(bytes)
            ?? throw new ImageValidationException("Unsupported image format. Use JPEG, PNG, WebP or GIF.");

        var fileName = $"{productId:N}-{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(System.IO.Path.Combine(_root, fileName), bytes, ct);
        return new StoredImage(fileName, contentType);
    }

    public Stream? OpenRead(string relativePath)
    {
        var full = Resolve(relativePath);
        return full is not null && File.Exists(full) ? File.OpenRead(full) : null;
    }

    public void Delete(string? relativePath)
    {
        var full = Resolve(relativePath);
        if (full is not null && File.Exists(full))
        {
            File.Delete(full);
        }
    }

    private string? Resolve(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, relativePath));
        // Guard against path traversal.
        return full.StartsWith(_root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    internal static (string Extension, string ContentType)? DetectFormat(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return (".jpg", "image/jpeg");
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return (".png", "image/png");
        if (b.Length >= 6 && (b[..6].SequenceEqual("GIF87a"u8) || b[..6].SequenceEqual("GIF89a"u8))) return (".gif", "image/gif");
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return (".webp", "image/webp");
        return null;
    }
}
