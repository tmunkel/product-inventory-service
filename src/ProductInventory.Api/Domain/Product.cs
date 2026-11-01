namespace ProductInventory.Api.Domain;

public class Product
{
    public Guid Id { get; set; }
    public string? Ean { get; set; }
    public required string Name { get; set; }
    public string? Brand { get; set; }
    public string? Category { get; set; }
    public string? Quantity { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public string? ImageContentType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
