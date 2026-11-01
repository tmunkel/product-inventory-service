using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProductInventory.Api.Contracts;
using ProductInventory.Api.Data;
using ProductInventory.Api.Domain;
using ProductInventory.Api.Images;
using ProductInventory.Api.OpenFoodFacts;

namespace ProductInventory.Api.Endpoints;

public static class ProductEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/products").WithTags("Products");

        products.MapGet("/", SearchAsync).WithName("SearchProducts")
            .WithSummary("Search products by name/brand (contains) or EAN (exact), optionally limited to specific ids (?ids=..&ids=..). Without filters all products are listed.");
        products.MapGet("/{id:guid}", GetByIdAsync).WithName("GetProduct");
        products.MapGet("/ean/{ean}", GetByEanAsync).WithName("GetProductByEan");
        products.MapPost("/", CreateAsync).WithName("CreateProduct");
        products.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateProduct");
        products.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteProduct");

        products.MapPost("/{id:guid}/image", UploadImageAsync).WithName("UploadProductImage")
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data");
        products.MapPost("/{id:guid}/image/from-url", ImportImageAsync).WithName("ImportProductImage")
            .WithSummary("Import an image from Open Food Facts.");
        products.MapGet("/{id:guid}/image", GetImageAsync).WithName("GetProductImage");
        products.MapDelete("/{id:guid}/image", DeleteImageAsync).WithName("DeleteProductImage");

        app.MapGet("/api/lookup/{ean}", LookupExternalAsync).WithTags("Lookup").WithName("LookupExternalProduct")
            .WithSummary("Look up product data for an EAN in Open Food Facts (used to prefill new products).");

        return app;
    }

    private static async Task<Ok<PagedResult<ProductDto>>> SearchAsync(
        InventoryDbContext db, string? query, Guid[]? ids, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var q = db.Products.AsNoTracking();
        var term = query?.Trim();

        if (ids is { Length: > 0 })
        {
            q = q.Where(p => ids.Contains(p.Id));
        }

        if (Ean.LooksLikeEan(term))
        {
            q = q.Where(p => p.Ean == term);
        }
        else if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{EscapeLike(term)}%";
            q = q.Where(p => EF.Functions.ILike(p.Name, pattern, "\\")
                || (p.Brand != null && EF.Functions.ILike(p.Brand, pattern, "\\")));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return TypedResults.Ok(new PagedResult<ProductDto>(items.Select(ProductDto.From).ToList(), total, page, pageSize));
    }

    private static async Task<Results<Ok<ProductDto>, NotFound>> GetByIdAsync(Guid id, InventoryDbContext db, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(ProductDto.From(product));
    }

    private static async Task<Results<Ok<ProductDto>, NotFound>> GetByEanAsync(string ean, InventoryDbContext db, CancellationToken ct)
    {
        var trimmed = ean.Trim();
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Ean == trimmed, ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(ProductDto.From(product));
    }

    private static async Task<Results<Created<ProductDto>, ValidationProblem, Conflict<ProblemDetailsLike>>> CreateAsync(
        ProductRequest request, InventoryDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (Validate(request) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = clock.GetUtcNow();
        var product = new Product { Id = Guid.CreateVersion7(), Name = request.Name.Trim(), CreatedAt = now, UpdatedAt = now };
        Apply(product, request);
        db.Products.Add(product);

        if (!await TrySaveAsync(db, ct))
        {
            return TypedResults.Conflict(DuplicateEan(product.Ean));
        }
        return TypedResults.Created($"/api/products/{product.Id}", ProductDto.From(product));
    }

    private static async Task<Results<Ok<ProductDto>, NotFound, ValidationProblem, Conflict<ProblemDetailsLike>>> UpdateAsync(
        Guid id, ProductRequest request, InventoryDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (Validate(request) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        Apply(product, request);
        product.UpdatedAt = clock.GetUtcNow();
        if (!await TrySaveAsync(db, ct))
        {
            return TypedResults.Conflict(DuplicateEan(product.Ean));
        }
        return TypedResults.Ok(ProductDto.From(product));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(Guid id, InventoryDbContext db, IImageStorage images, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return TypedResults.NotFound();
        }
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        images.Delete(product.ImagePath);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ProductDto>, NotFound, ValidationProblem>> UploadImageAsync(
        Guid id, IFormFile? file, InventoryDbContext db, IImageStorage images, TimeProvider clock, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["An image file is required."] });
        }
        await using var stream = file.OpenReadStream();
        return await StoreImageAsync(id, stream, db, images, clock, ct);
    }

    private static async Task<Results<Ok<ProductDto>, NotFound, ValidationProblem>> ImportImageAsync(
        Guid id, ImageFromUrlRequest request, InventoryDbContext db, IImageStorage images,
        IExternalProductLookup lookup, TimeProvider clock, CancellationToken ct)
    {
        if (!lookup.IsAllowedImageUrl(request.Url))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["Only https Open Food Facts image URLs are allowed."] });
        }
        if (!await db.Products.AnyAsync(p => p.Id == id, ct))
        {
            return TypedResults.NotFound();
        }
        var downloaded = await lookup.DownloadImageAsync(request.Url, ct);
        if (downloaded is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["The image could not be downloaded."] });
        }
        using var stream = new MemoryStream(downloaded.Value.Bytes);
        return await StoreImageAsync(id, stream, db, images, clock, ct);
    }

    private static async Task<Results<Ok<ProductDto>, NotFound, ValidationProblem>> StoreImageAsync(
        Guid id, Stream content, InventoryDbContext db, IImageStorage images, TimeProvider clock, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        StoredImage stored;
        try
        {
            stored = await images.SaveAsync(id, content, ct);
        }
        catch (ImageValidationException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [ex.Message] });
        }

        var previous = product.ImagePath;
        product.ImagePath = stored.RelativePath;
        product.ImageContentType = stored.ContentType;
        product.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        images.Delete(previous);
        return TypedResults.Ok(ProductDto.From(product));
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> GetImageAsync(
        Guid id, InventoryDbContext db, IImageStorage images, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product?.ImagePath is null || images.OpenRead(product.ImagePath) is not { } stream)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Stream(stream, product.ImageContentType ?? "application/octet-stream",
            lastModified: product.UpdatedAt);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteImageAsync(
        Guid id, InventoryDbContext db, IImageStorage images, TimeProvider clock, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return TypedResults.NotFound();
        }
        var previous = product.ImagePath;
        product.ImagePath = null;
        product.ImageContentType = null;
        product.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        images.Delete(previous);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ExternalProductInfo>, NotFound, ValidationProblem>> LookupExternalAsync(
        string ean, IExternalProductLookup lookup, CancellationToken ct)
    {
        if (!Ean.IsValid(ean))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["ean"] = ["Invalid EAN."] });
        }
        var info = await lookup.LookupAsync(ean.Trim(), ct);
        return info is null ? TypedResults.NotFound() : TypedResults.Ok(info);
    }

    private static Dictionary<string, string[]>? Validate(ProductRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors["name"] = ["Name is required."];
        else if (request.Name.Trim().Length > 200)
            errors["name"] = ["Name must be at most 200 characters."];
        if (!string.IsNullOrWhiteSpace(request.Ean) && !Ean.IsValid(request.Ean))
            errors["ean"] = ["EAN must be 8, 12, 13 or 14 digits with a valid check digit."];
        if (request.Brand?.Trim().Length > 200) errors["brand"] = ["Brand must be at most 200 characters."];
        if (request.Category?.Trim().Length > 200) errors["category"] = ["Category must be at most 200 characters."];
        if (request.Quantity?.Trim().Length > 50) errors["quantity"] = ["Quantity must be at most 50 characters."];
        if (request.Description?.Trim().Length > 2000) errors["description"] = ["Description must be at most 2000 characters."];
        return errors.Count > 0 ? errors : null;
    }

    private static void Apply(Product product, ProductRequest request)
    {
        product.Name = request.Name.Trim();
        product.Ean = Clean(request.Ean);
        product.Brand = Clean(request.Brand);
        product.Category = Clean(request.Category);
        product.Quantity = Clean(request.Quantity);
        product.Description = Clean(request.Description);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static async Task<bool> TrySaveAsync(InventoryDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    private static ProblemDetailsLike DuplicateEan(string? ean) =>
        new("Duplicate EAN", $"A product with EAN {ean} already exists.", StatusCodes.Status409Conflict);
}

public record ProblemDetailsLike(string Title, string Detail, int Status);
