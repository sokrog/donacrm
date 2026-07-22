namespace Dona.Crm.Web.Domain;

public static class CurrencyCodes
{
    public static readonly IReadOnlyList<string> Supported = ["UZS", "CNY", "USD", "EUR", "RUB", "KZT", "TRY", "KRW"];

    public static string Normalize(string? value, string fallback = "UZS")
    {
        var code = value?.Trim().ToUpperInvariant();
        return Supported.Contains(code ?? string.Empty, StringComparer.Ordinal) ? code! : fallback;
    }

    public static string Symbol(string? code) => Normalize(code) switch
    {
        "UZS" => "сум", "CNY" => "¥", "USD" => "$", "EUR" => "€", "RUB" => "₽", "KZT" => "₸", "TRY" => "₺", "KRW" => "₩", _ => Normalize(code)
    };
}
