using System.Text;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed record LocalImage(byte[] Content, string ContentType);

/// <summary>Хранилище байтов локальных фотографий (файлы на устройстве или IndexedDB в браузере).</summary>
public interface ILocalImageStore
{
    Task SaveAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default);
    Task<LocalImage?> ReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default);
}

/// <summary>Правила непрозрачных ключей локальных фотографий и формат ссылки <c>local:&lt;key&gt;</c>.</summary>
public static class LocalImageKey
{
    public const string UrlPrefix = "local:";
    public const int MaxLength = 120;

    public static bool IsValid(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxLength || key.All(c => c == '.'))
            return false;
        foreach (var c in key)
        {
            if (!IsAllowed(c))
                return false;
        }
        return true;
    }

    public static string Validate(string? key) =>
        IsValid(key) ? key! : throw new InvalidDataException("Некорректный ключ локального изображения.");

    /// <summary>Приводит произвольный StorageKey (например, <c>browser:id:guid</c>) к допустимому ключу.</summary>
    public static string Sanitize(string? value)
    {
        var builder = new StringBuilder();
        foreach (var c in value ?? string.Empty)
            builder.Append(IsAllowed(c) ? c : '-');
        var key = builder.ToString().TrimStart('.');
        if (key.Length > MaxLength)
            key = key[^MaxLength..];
        return IsValid(key) ? key : $"image-{Guid.NewGuid():N}";
    }

    public static string ToUrl(string key) => UrlPrefix + key;

    public static bool TryParseUrl(string? url, out string key)
    {
        key = string.Empty;
        if (url is null || !url.StartsWith(UrlPrefix, StringComparison.Ordinal))
            return false;
        var candidate = url[UrlPrefix.Length..];
        if (!IsValid(candidate))
            return false;
        key = candidate;
        return true;
    }

    public static string ContentTypeFromKey(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".heic" or ".heif" => "image/heic",
        _ => "image/jpeg"
    };

    public static bool TryParseDataUrl(string? url, out string contentType, out byte[] content)
    {
        contentType = string.Empty;
        content = [];
        if (url is null || !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;
        var comma = url.IndexOf(',');
        if (comma < 0)
            return false;
        var header = url[5..comma];
        if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            content = Convert.FromBase64String(url[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }
        var type = header[..^";base64".Length];
        contentType = string.IsNullOrWhiteSpace(type) ? "application/octet-stream" : type;
        return true;
    }

    public static string ToDataUrl(byte[] content, string contentType) =>
        $"data:{contentType};base64,{Convert.ToBase64String(content)}";

    /// <summary>Все ключи локальных фотографий, на которые ссылается снимок (после миграции это local:-ссылки).</summary>
    public static HashSet<string> CollectReferencedKeys(DonaSyncSnapshot snapshot)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in EnumerateImages(snapshot))
        {
            if (image.Storage == ProductImageStorage.Local && TryParseUrl(image.Url, out var key))
                keys.Add(key);
        }
        return keys;
    }

    public static IEnumerable<ProductImage> EnumerateImages(DonaSyncSnapshot snapshot) =>
        snapshot.Products.SelectMany(item => item.Images)
            .Concat(snapshot.Marketing.Collections.SelectMany(item => item.Images))
            .Concat(snapshot.Marketing.Outfits.SelectMany(item => item.Images));

    private static bool IsAllowed(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.';
}

/// <summary>Хранит фотографии файлами в одной папке. Ключ не может содержать разделителей пути.</summary>
public sealed class FileLocalImageStore(string directory) : ILocalImageStore
{
    private readonly string root = Path.GetFullPath(directory);

    public async Task SaveAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
    }

    public async Task<LocalImage?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;
        return new LocalImage(await File.ReadAllBytesAsync(path, cancellationToken), LocalImageKey.ContentTypeFromKey(key));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> keys = Directory.Exists(root)
            ? Directory.EnumerateFiles(root).Select(Path.GetFileName).OfType<string>().Where(LocalImageKey.IsValid).ToList()
            : [];
        return Task.FromResult(keys);
    }

    private string PathFor(string key)
    {
        LocalImageKey.Validate(key);
        var path = Path.GetFullPath(Path.Combine(root, key));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Некорректный путь локального изображения.");
        return path;
    }
}
