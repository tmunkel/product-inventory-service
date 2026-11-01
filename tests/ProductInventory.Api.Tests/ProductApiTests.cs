using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductInventory.Api.Contracts;

namespace ProductInventory.Api.Tests;

public class ProductApiTests(InventoryApiFactory factory) : IClassFixture<InventoryApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string NewEan()
    {
        // Random EAN-13 with valid check digit.
        var digits = string.Concat(Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)));
        var sum = 0;
        for (var i = 11; i >= 0; i--)
        {
            sum += (digits[i] - '0') * ((11 - i) % 2 == 0 ? 3 : 1);
        }
        return digits + (10 - sum % 10) % 10;
    }

    private async Task<ProductDto> CreateAsync(string name, string? ean = null, string? brand = null)
    {
        var response = await _client.PostAsJsonAsync("/api/products", new ProductRequest(ean, name, brand, null, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    [Fact]
    public async Task Create_and_get_product()
    {
        var ean = NewEan();
        var created = await CreateAsync("Whole Milk", ean, "Dairy Co");

        var byId = await _client.GetFromJsonAsync<ProductDto>($"/api/products/{created.Id}");
        var byEan = await _client.GetFromJsonAsync<ProductDto>($"/api/products/ean/{ean}");

        Assert.Equal("Whole Milk", byId!.Name);
        Assert.Equal(created.Id, byEan!.Id);
        Assert.False(byId.HasImage);
    }

    [Fact]
    public async Task Search_by_ean_is_exact()
    {
        var ean = NewEan();
        var created = await CreateAsync("Exact Ean Product", ean);
        await CreateAsync("Other Product", NewEan());

        var result = await _client.GetFromJsonAsync<PagedResult<ProductDto>>($"/api/products?query={ean}");

        var item = Assert.Single(result!.Items);
        Assert.Equal(created.Id, item.Id);
    }

    [Fact]
    public async Task Search_by_name_is_case_insensitive_contains_and_matches_brand()
    {
        var marker = "m" + Guid.NewGuid().ToString("N")[..8];
        await CreateAsync($"Organic Banana {marker}");
        await CreateAsync($"Banana Bread {marker}");
        await CreateAsync("Apple Juice", brand: $"Brand{marker}");

        var bananas = await _client.GetFromJsonAsync<PagedResult<ProductDto>>($"/api/products?query=banana%20{marker}");
        var byName = await _client.GetFromJsonAsync<PagedResult<ProductDto>>($"/api/products?query={marker.ToUpperInvariant()}");

        Assert.Single(bananas!.Items);
        Assert.Equal(3, byName!.Total);
    }

    [Fact]
    public async Task Search_escapes_like_wildcards()
    {
        var marker = "m" + Guid.NewGuid().ToString("N")[..8];
        await CreateAsync($"Plain {marker}");

        var result = await _client.GetFromJsonAsync<PagedResult<ProductDto>>($"/api/products?query={marker}%25");

        Assert.Empty(result!.Items);
    }

    [Fact]
    public async Task Duplicate_ean_returns_conflict()
    {
        var ean = NewEan();
        await CreateAsync("First", ean);

        var response = await _client.PostAsJsonAsync("/api/products", new ProductRequest(ean, "Second", null, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("4006381333932", "Name")]
    [InlineData(null, "")]
    public async Task Invalid_request_returns_validation_problem(string? ean, string name)
    {
        var response = await _client.PostAsJsonAsync("/api/products", new ProductRequest(ean, name, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_and_delete_product()
    {
        var created = await CreateAsync("Old name");

        var update = await _client.PutAsJsonAsync($"/api/products/{created.Id}", new ProductRequest(null, "New name", "B", "C", "1 l", "D"));
        var updated = await update.Content.ReadFromJsonAsync<ProductDto>();
        var delete = await _client.DeleteAsync($"/api/products/{created.Id}");
        var get = await _client.GetAsync($"/api/products/{created.Id}");

        Assert.Equal("New name", updated!.Name);
        Assert.Equal("1 l", updated.Quantity);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Upload_and_download_image()
    {
        var created = await CreateAsync("With image");
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestImages.Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "photo.png");

        var upload = await _client.PostAsync($"/api/products/{created.Id}/image", content);
        var dto = await upload.Content.ReadFromJsonAsync<ProductDto>();
        var image = await _client.GetAsync(dto!.ImageUrl);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.True(dto.HasImage);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestImages.Png, await image.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_rejects_non_images_and_oversized_files()
    {
        var created = await CreateAsync("Bad image");

        using var text = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "a.png" } };
        using var big = new MultipartFormDataContent { { new ByteArrayContent([.. TestImages.Png, .. new byte[2048]]), "file", "b.png" } };

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/products/{created.Id}/image", text)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/products/{created.Id}/image", big)).StatusCode);
    }

    [Fact]
    public async Task Import_image_only_from_allowed_hosts()
    {
        var created = await CreateAsync("Imported image");

        var bad = await _client.PostAsJsonAsync($"/api/products/{created.Id}/image/from-url", new ImageFromUrlRequest("http://169.254.169.254/x.png"));
        var good = await _client.PostAsJsonAsync($"/api/products/{created.Id}/image/from-url", new ImageFromUrlRequest("https://images.openfoodfacts.org/x.png"));

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
        Assert.True((await good.Content.ReadFromJsonAsync<ProductDto>())!.HasImage);
    }

    [Fact]
    public async Task External_lookup_returns_prefill_data()
    {
        var ean = NewEan();
        factory.ExternalLookup.Products[ean] = new ExternalProductInfo(ean, "Nutella", "Ferrero", "Spreads", "400 g", null, "OpenFoodFacts");

        var found = await _client.GetFromJsonAsync<ExternalProductInfo>($"/api/lookup/{ean}");
        var missing = await _client.GetAsync($"/api/lookup/{NewEan()}");
        var invalid = await _client.GetAsync("/api/lookup/123");

        Assert.Equal("Nutella", found!.Name);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}

