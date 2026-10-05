using Dona.Crm.Web.Domain;
using System.Text.RegularExpressions;

namespace Dona.Crm.Web.Services;

public static partial class SaleNumberGenerator
{
    public static string NormalizePrefix(string? prefix)
    {
        var normalized = InvalidPrefixCharacters().Replace(prefix?.Trim().ToUpperInvariant() ?? string.Empty, string.Empty).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "SALE" : normalized[..Math.Min(normalized.Length, 12)];
    }

    public static string Generate(IEnumerable<Sale> existingSales, string? prefix, DateTimeOffset now)
    {
        var stem = $"{NormalizePrefix(prefix)}-{now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)}-";
        var last = existingSales.Select(x => x.Number).Where(x => x.StartsWith(stem, StringComparison.OrdinalIgnoreCase)).Select(x => int.TryParse(x[stem.Length..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0).DefaultIfEmpty().Max();
        return $"{stem}{last + 1:D3}";
    }

    [GeneratedRegex("[^A-Z0-9-]+", RegexOptions.CultureInvariant)] private static partial Regex InvalidPrefixCharacters();
}
