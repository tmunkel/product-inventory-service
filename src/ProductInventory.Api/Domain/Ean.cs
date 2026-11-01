namespace ProductInventory.Api.Domain;

/// <summary>GTIN helpers for EAN-8, UPC-A (12), EAN-13 and GTIN-14.</summary>
public static class Ean
{
    private static readonly int[] ValidLengths = [8, 12, 13, 14];

    public static bool LooksLikeEan(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && ValidLengths.Contains(trimmed.Length)
            && trimmed.All(char.IsAsciiDigit);
    }

    public static bool IsValid(string? value)
    {
        if (!LooksLikeEan(value))
        {
            return false;
        }

        var digits = value!.Trim();
        var sum = 0;
        // Weights alternate 3,1,3,... starting from the digit left of the check digit.
        for (int i = digits.Length - 2, weight = 3; i >= 0; i--, weight = weight == 3 ? 1 : 3)
        {
            sum += (digits[i] - '0') * weight;
        }

        var check = (10 - sum % 10) % 10;
        return check == digits[^1] - '0';
    }
}
