using ProductInventory.Api.Domain;

namespace ProductInventory.Api.Tests;

public class EanTests
{
    [Theory]
    [InlineData("4006381333931")] // EAN-13
    [InlineData("96385074")]      // EAN-8
    [InlineData("036000291452")]  // UPC-A
    [InlineData("10012345678902")] // GTIN-14
    public void Valid_codes_are_accepted(string ean) => Assert.True(Ean.IsValid(ean));

    [Theory]
    [InlineData("4006381333932")]
    [InlineData("12345")]
    [InlineData("40063813339a1")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_codes_are_rejected(string? ean) => Assert.False(Ean.IsValid(ean));
}
