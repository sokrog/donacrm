using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public interface ISalesRepository
{
    Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default);
    Task UpsertCustomerAsync(Customer customer, CancellationToken cancellationToken = default);
    Task DeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken = default);
    Task<Sale?> GetSaleAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpsertSaleAsync(Sale sale, CancellationToken cancellationToken = default);
}

public sealed class SalesData
{
    public List<Customer> Customers { get; set; } = [];
    public List<Sale> Sales { get; set; } = [];
}
