using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class CurrencyContext(IBusinessSettingsRepository settings)
{
    private bool _loaded;
    public string Code { get; private set; } = "UZS";
    public string Symbol => CurrencyCodes.Symbol(Code);

    public async Task EnsureLoadedAsync(CancellationToken token = default)
    {
        if (_loaded) return;
        var business = await settings.GetAsync(token);
        Set(business.MainCurrencyCode);
    }

    public void Set(string? code) { BusinessSettings.EnsureAccountingCurrency(code); Code = BusinessSettings.AccountingCurrency; _loaded = true; }
    public string Format(decimal? value) => $"{value ?? 0:N0} {Symbol}";
    public string SourceAmount(decimal? value, string? code) => $"{value ?? 0:N2} {CurrencyCodes.Symbol(code)}";
}
