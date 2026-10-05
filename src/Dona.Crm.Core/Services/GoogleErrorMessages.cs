using System.Net;
using System.Text.Json;

namespace Dona.Crm.Web.Services;

/// <summary>Google API failure with a user-safe Russian message; the HTTP status stays available for callers.</summary>
public sealed class GoogleApiException(string message, HttpStatusCode statusCode) : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

/// <summary>Turns Google HTTP error responses into short Russian messages without leaking raw JSON.</summary>
public static class GoogleErrorMessages
{
    private const int MaxDetailLength = 200;

    public static string Describe(HttpStatusCode statusCode, string? body, string prefix)
    {
        var (reasons, detail) = Parse(body);
        var status = (int)statusCode;
        var isInvalidGrant = reasons.Contains("invalid_grant");

        if (isInvalidGrant)
            return "Доступ к Google отозван или истёк. Войдите заново.";
        if (detail?.Contains("insufficient authentication scopes", StringComparison.OrdinalIgnoreCase) == true ||
            reasons.Contains("insufficientPermissions") && detail?.Contains("scope", StringComparison.OrdinalIgnoreCase) == true)
        {
            return $"{prefix}: аккаунт не выдал разрешение на Google Drive (данные приложения). Повторите вход и отметьте все запрошенные разрешения Google Drive.";
        }
        if (status == 401 && !reasons.Contains("invalid_client"))
            return "Сессия Google истекла. Войдите в Google ещё раз.";
        if (status == 429 ||
            status == 403 && reasons.Overlaps(["rateLimitExceeded", "userRateLimitExceeded", "quotaExceeded", "RESOURCE_EXHAUSTED"]))
        {
            return "Google временно ограничил запросы. Повторите через минуту.";
        }
        if (status == 404)
            return "Файл в Google Drive не найден.";
        if (status >= 500)
            return "Сервис Google временно недоступен. Повторите позже.";
        return string.IsNullOrWhiteSpace(detail) ? $"{prefix} (HTTP {status})." : $"{prefix}: {detail}";
    }

    public static async Task<GoogleApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        string prefix,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new GoogleApiException(Describe(response.StatusCode, body, prefix), response.StatusCode);
    }

    private static (HashSet<string> Reasons, string? Detail) Parse(string? body)
    {
        var reasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? detail = null;
        if (string.IsNullOrWhiteSpace(body))
            return (reasons, null);
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (reasons, Truncate(body));

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    detail = GetString(error, "message");
                    AddReason(reasons, GetString(error, "status"));
                    if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in errors.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.Object)
                                AddReason(reasons, GetString(item, "reason"));
                    }
                }
                else if (error.ValueKind == JsonValueKind.String)
                {
                    AddReason(reasons, error.GetString());
                    detail = error.GetString();
                }
            }
            var description = GetString(root, "error_description");
            if (!string.IsNullOrWhiteSpace(description))
                detail = description;
        }
        catch (JsonException)
        {
            return (reasons, Truncate(body));
        }
        return (reasons, Truncate(detail));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void AddReason(HashSet<string> reasons, string? reason)
    {
        if (!string.IsNullOrWhiteSpace(reason))
            reasons.Add(reason);
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length <= MaxDetailLength ? value : value[..MaxDetailLength] + "…";
    }
}
