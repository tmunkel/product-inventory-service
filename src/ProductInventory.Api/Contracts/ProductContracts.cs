using ProductInventory.Api.Domain;

namespace ProductInventory.Api.Contracts;

public record ProductDto(
    Guid Id,
    string? Ean,
    string Name,
    string? Brand,
    string? Category,
    string? Quantity,
    string? Description,
    bool HasImage,
    string? ImageUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ProductDto From(Product p) => new(
        p.Id, p.Ean, p.Name, p.Brand, p.Category, p.Quantity, p.Description,
        p.ImagePath is not null,
        p.ImagePath is null ? null : $"/api/products/{p.Id}/image?v={p.UpdatedAt.ToUnixTimeSeconds()}",
        p.CreatedAt, p.UpdatedAt);
}

public record ProductRequest(
    string? Ean,
    string Name,
    string? Brand,
    string? Category,
    string? Quantity,
    string? Description);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record ImageFromUrlRequest(string Url);

public record ExternalProductInfo(
    string Ean,
    string? Name,
    string? Brand,
    string? Category,
    string? Quantity,
    string? ImageUrl,
    string Source);
