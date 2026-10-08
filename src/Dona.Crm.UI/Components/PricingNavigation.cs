using Dona.Crm.Web.Domain;

namespace Dona.Crm.UI.Components;

/// <summary>Only known pricing routes can be used as return destinations.</summary>
public static class PricingNavigation
{
    public static string Context(PricingSource source, Guid? sourceId, string? ids, string? query, string? filter, string? sort) =>
        $"?entry={source}&sourceId={sourceId}&ids={Encode(ids)}&q={Encode(query)}&filter={Encode(filter)}&sort={Encode(sort)}";

    public static string? Editor(string? entry, Guid? sourceId, string? ids, string? query, string? filter, string? sort) => entry switch
    {
        nameof(PricingSource.Product) when sourceId is { } id && id != Guid.Empty => $"/products/{id}/pricing",
        nameof(PricingSource.Purchase) when sourceId is { } id && id != Guid.Empty => $"/purchases/{id}/pricing",
        nameof(PricingSource.Catalog) => $"/products/pricing?ids={Encode(ids)}&q={Encode(query)}&filter={Encode(filter)}&sort={Encode(sort)}",
        _ => null
    };

    private static string Encode(string? value) => Uri.EscapeDataString(value ?? "");
}
