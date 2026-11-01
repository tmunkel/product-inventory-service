using System.Text.Json.Serialization;
using ProductInventory.Api.Contracts;

namespace ProductInventory.Api.OpenFoodFacts;

public class OpenFoodFactsOptions
{
    public const string SectionName = "OpenFoodFacts";
    public string BaseUrl { get; set; } = "https://world.openfoodfacts.org/";
    public string UserAgent { get; set; } = "ProductInventoryService/1.0 (self-hosted grocery app)";
}

public interface IExternalProductLookup
{
    Task<ExternalProductInfo?> LookupAsync(string ean, CancellationToken ct);
    Task<(byte[] Bytes, string? ContentType)?> DownloadImageAsync(string url, CancellationToken ct);
    bool IsAllowedImageUrl(string url);
}

public class OpenFoodFactsClient(HttpClient http, ILogger<OpenFoodFactsClient> logger) : IExternalProductLookup
{
    public async Task<ExternalProductInfo?> LookupAsync(string ean, CancellationToken ct)
    {
        try
        {
            var response = await http.GetFromJsonAsync<OffResponse>(
                $"api/v2/product/{Uri.EscapeDataString(ean)}?fields=product_name,brands,categories,quantity,image_front_url,image_url", ct);
            if (response is not { Status: 1, Product: not null })
            {
                return null;
            }

            var p = response.Product;
            return new ExternalProductInfo(
                ean,
                NullIfEmpty(p.ProductName),
                NullIfEmpty(p.Brands?.Split(',')[0].Trim()),
                // Most specific category without a language tag such as "fr:".
                NullIfEmpty(p.Categories?.Split(',').Select(c => c.Trim()).LastOrDefault(c => c.Length > 0 && !c.Contains(':'))),
                NullIfEmpty(p.Quantity),
                NullIfEmpty(p.ImageFrontUrl) ?? NullIfEmpty(p.ImageUrl),
                "OpenFoodFacts");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Open Food Facts lookup failed for {Ean}", ean);
            return null;
        }
    }

    public bool IsAllowedImageUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("openfoodfacts.org", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".openfoodfacts.org", StringComparison.OrdinalIgnoreCase));

    public async Task<(byte[] Bytes, string? ContentType)?> DownloadImageAsync(string url, CancellationToken ct)
    {
        if (!IsAllowedImageUrl(url))
        {
            return null;
        }
        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return (await response.Content.ReadAsByteArrayAsync(ct), response.Content.Headers.ContentType?.MediaType);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed class OffResponse
    {
        [JsonPropertyName("status")] public int Status { get; set; }
        [JsonPropertyName("product")] public OffProduct? Product { get; set; }
    }

    private sealed class OffProduct
    {
        [JsonPropertyName("product_name")] public string? ProductName { get; set; }
        [JsonPropertyName("brands")] public string? Brands { get; set; }
        [JsonPropertyName("categories")] public string? Categories { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("image_front_url")] public string? ImageFrontUrl { get; set; }
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    }
}
