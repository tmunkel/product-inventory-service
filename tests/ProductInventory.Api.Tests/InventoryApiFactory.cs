using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductInventory.Api.Contracts;
using ProductInventory.Api.OpenFoodFacts;
using Testcontainers.PostgreSql;

namespace ProductInventory.Api.Tests;

public sealed class InventoryApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string ImageDirectory { get; } = Path.Combine(Path.GetTempPath(), "inventory-tests-" + Guid.NewGuid().ToString("N"));

    public FakeExternalLookup ExternalLookup { get; } = new();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Inventory", _postgres.GetConnectionString());
        builder.UseSetting("ImageStorage:Path", ImageDirectory);
        builder.UseSetting("ImageStorage:MaxBytes", "1024");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IExternalProductLookup>();
            services.AddSingleton<IExternalProductLookup>(ExternalLookup);
        });
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(ImageDirectory))
        {
            Directory.Delete(ImageDirectory, recursive: true);
        }
    }
}

public sealed class FakeExternalLookup : IExternalProductLookup
{
    public Dictionary<string, ExternalProductInfo> Products { get; } = new();
    public byte[] ImageBytes { get; set; } = TestImages.Png;

    public Task<ExternalProductInfo?> LookupAsync(string ean, CancellationToken ct) =>
        Task.FromResult(Products.GetValueOrDefault(ean));

    public Task<(byte[] Bytes, string? ContentType)?> DownloadImageAsync(string url, CancellationToken ct) =>
        Task.FromResult<(byte[], string?)?>((ImageBytes, "image/png"));

    public bool IsAllowedImageUrl(string url) => url.StartsWith("https://images.openfoodfacts.org/", StringComparison.Ordinal);
}

public static class TestImages
{
    // Minimal 1x1 PNG.
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
