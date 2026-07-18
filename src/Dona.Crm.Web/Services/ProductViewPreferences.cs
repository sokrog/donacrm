using Microsoft.JSInterop;

namespace Dona.Crm.Web.Services;

public enum ProductViewMode { List, Grid }

public sealed class ProductViewPreferences(IJSRuntime js)
{
    private const string ModeKey = "dona.products.view";
    private const string ColumnsKey = "dona.products.columns";
    public ProductViewMode Mode { get; private set; } = ProductViewMode.List;
    public int Columns { get; private set; } = 3;
    public bool IsLoaded { get; private set; }

    public async Task LoadAsync()
    {
        if (IsLoaded) return;
        var mode = await js.InvokeAsync<string?>("localStorage.getItem", ModeKey);
        var columns = await js.InvokeAsync<string?>("localStorage.getItem", ColumnsKey);
        if (Enum.TryParse<ProductViewMode>(mode, true, out var parsedMode)) Mode = parsedMode;
        if (int.TryParse(columns, out var parsedColumns)) Columns = Math.Clamp(parsedColumns, 2, 4);
        IsLoaded = true;
    }

    public async Task SetModeAsync(ProductViewMode mode) { Mode = mode; await js.InvokeVoidAsync("localStorage.setItem", ModeKey, mode.ToString()); }
    public async Task SetColumnsAsync(int columns) { Columns = Math.Clamp(columns, 2, 4); await js.InvokeVoidAsync("localStorage.setItem", ColumnsKey, Columns.ToString()); }
}
