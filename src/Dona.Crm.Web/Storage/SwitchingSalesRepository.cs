using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Web.Storage;

public sealed class SwitchingSalesRepository(GoogleSheetsSettingsStore settings, JsonSalesRepository local, GoogleSheetsSalesRepository google, LoadingState loading) : ISalesRepository
{
    private ISalesRepository Current => settings.UseGoogleSheets ? google : local;
    public Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken token = default) => loading.RunAsync("Загружаем покупателей…", () => Current.GetCustomersAsync(token));
    public Task UpsertCustomerAsync(Customer customer, CancellationToken token = default) => loading.RunAsync("Сохраняем покупателя…", () => Current.UpsertCustomerAsync(customer, token));
    public Task DeleteCustomerAsync(Guid id, CancellationToken token = default) => loading.RunAsync("Удаляем покупателя…", () => Current.DeleteCustomerAsync(id, token));
    public Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken token = default) => loading.RunAsync("Загружаем продажи…", () => Current.GetSalesAsync(token));
    public Task<Sale?> GetSaleAsync(Guid id, CancellationToken token = default) => loading.RunAsync("Загружаем заказ…", () => Current.GetSaleAsync(id, token));
    public Task UpsertSaleAsync(Sale sale, CancellationToken token = default) => loading.RunAsync("Сохраняем заказ…", () => Current.UpsertSaleAsync(sale, token));
}
