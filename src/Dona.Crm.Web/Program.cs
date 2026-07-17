using Dona.Crm.Web.Components;
using Dona.Crm.Web.Storage;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "data-protection")));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<GoogleSheetsOptions>(builder.Configuration.GetSection(GoogleSheetsOptions.SectionName));
builder.Services.AddSingleton<GoogleSheetsSettingsStore>();
builder.Services.AddSingleton<JsonCatalogRepository>();
builder.Services.AddSingleton<GoogleSheetsCatalogRepository>();
builder.Services.AddSingleton<CatalogMigrationService>();
builder.Services.AddSingleton<ICatalogRepository>(services =>
{
    var storage = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
    return storage.Provider.Equals("GoogleSheets", StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<GoogleSheetsCatalogRepository>()
        : services.GetRequiredService<JsonCatalogRepository>();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
