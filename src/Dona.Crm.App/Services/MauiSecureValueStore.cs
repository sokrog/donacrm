using Dona.Crm.Web.Services;

namespace Dona.Crm.App.Services;

public sealed class MauiSecureValueStore : ISecureValueStore
{
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch
        {
            SecureStorage.Default.Remove(key);
            return null;
        }
    }

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public bool Remove(string key) => SecureStorage.Default.Remove(key);
}
