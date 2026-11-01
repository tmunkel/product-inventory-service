using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ProductInventory.Api.OpenFoodFacts;

namespace ProductInventory.Api.Tests;

public class OpenFoodFactsClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static OpenFoodFactsClient Create(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://world.openfoodfacts.org/") }, NullLogger<OpenFoodFactsClient>.Instance);

    [Fact]
    public async Task Maps_product_fields()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"status":1,"product":{"product_name":"Nutella","brands":"Ferrero, Nutella","categories":"Spreads, Sweet spreads, fr:Nuttela","quantity":"400 g","image_front_url":"https://images.openfoodfacts.org/a.jpg"}}
            """);

        var info = await Create(handler).LookupAsync("3017620422003", CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("Nutella", info.Name);
        Assert.Equal("Ferrero", info.Brand);
        Assert.Equal("Sweet spreads", info.Category);
        Assert.Equal("400 g", info.Quantity);
        Assert.Equal("https://images.openfoodfacts.org/a.jpg", info.ImageUrl);
        Assert.Contains("api/v2/product/3017620422003", handler.LastUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"status":0}""")]
    [InlineData(HttpStatusCode.NotFound, """{"status":0}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Returns_null_when_not_found_or_failing(HttpStatusCode status, string body)
    {
        Assert.Null(await Create(new StubHandler(status, body)).LookupAsync("3017620422003", CancellationToken.None));
    }

    [Theory]
    [InlineData("https://images.openfoodfacts.org/x.jpg", true)]
    [InlineData("https://openfoodfacts.org/x.jpg", true)]
    [InlineData("http://images.openfoodfacts.org/x.jpg", false)]
    [InlineData("https://evil.com/openfoodfacts.org/x.jpg", false)]
    [InlineData("https://openfoodfacts.org.evil.com/x.jpg", false)]
    public void Only_open_food_facts_https_urls_are_allowed(string url, bool allowed) =>
        Assert.Equal(allowed, Create(new StubHandler(HttpStatusCode.OK, "")).IsAllowedImageUrl(url));
}
