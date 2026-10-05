using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;

namespace Dona.Crm.App.Services;

public sealed class GooglePlatformAuthorization : IGooglePlatformAuthorization
{
    private const int AuthorizationRequestCode = 40721;
    private const int NetworkErrorStatusCode = 7;
    private const int InternalErrorStatusCode = 8;
    private const int DeveloperErrorStatusCode = 10;
    private const int CanceledStatusCode = 16;
    private static readonly string[] ScopeNames =
    [
        "openid",
        "email",
        "https://www.googleapis.com/auth/drive.appdata",
        "https://www.googleapis.com/auth/drive.file"
    ];

    public bool RequiresClientId => false;

    public async Task<GooglePlatformToken> AuthorizeAsync(
        string? clientId,
        string? clientSecret,
        bool interactive,
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        var activity = Platform.CurrentActivity as MainActivity
            ?? throw new InvalidOperationException("Главное окно Android недоступно.");
        var client = Identity.GetAuthorizationClient(activity);
        var scopes = ScopeNames.Select(scope => new Scope(scope)).ToList();
        using var request = AuthorizationRequest.InvokeBuilder()
            .SetRequestedScopes(scopes)
            .Build();

        AuthorizationResult result;
        try
        {
            result = await AuthorizeWithRetryAsync(
                activity,
                client,
                request,
                interactive,
                cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == CanceledStatusCode)
        {
            throw new OperationCanceledException("Вход в Google отменён.", exception, cancellationToken);
        }
        catch (ApiException exception)
        {
            throw new InvalidOperationException(BuildAuthorizationError(exception.StatusCode), exception);
        }

        if (string.IsNullOrWhiteSpace(result.AccessToken))
            throw new InvalidOperationException("Google Play Services не вернул access token.");
        return new(result.AccessToken, DateTimeOffset.UtcNow.AddMinutes(50));
    }

    public async Task DisconnectAsync(string? accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return;
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("Главное окно Android недоступно.");
        var client = Identity.GetAuthorizationClient(activity);
        using var request = ClearTokenRequest.InvokeBuilder().SetToken(accessToken).Build();
        await client.ClearToken(request).AsAsync();
    }

    private static async Task<AuthorizationResult> AuthorizeWithRetryAsync(
        MainActivity activity,
        IAuthorizationClient client,
        AuthorizationRequest request,
        bool interactive,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var result = await client.Authorize(request).AsAsync<AuthorizationResult>();
                if (!result.HasResolution)
                    return result;
                if (!interactive)
                    throw new InvalidOperationException("Сессия Google завершена. Выполните вход ещё раз.");
                return await ResolveAsync(activity, client, result, cancellationToken);
            }
            catch (ApiException exception) when (
                exception.StatusCode == InternalErrorStatusCode &&
                attempt == 0)
            {
                // Google defines status 8 as a transient internal error and recommends retrying.
                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
            }
        }
    }

    private static async Task<AuthorizationResult> ResolveAsync(
        MainActivity activity,
        IAuthorizationClient client,
        AuthorizationResult initial,
        CancellationToken cancellationToken)
    {
        var pendingIntent = initial.PendingIntent
            ?? throw new InvalidOperationException("Google Play Services не вернул окно подтверждения.");
        var completion = new TaskCompletionSource<ActivityResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnResult(int requestCode, Result resultCode, Intent? data)
        {
            if (requestCode != AuthorizationRequestCode)
                return;
            completion.TrySetResult(new ActivityResult(resultCode, data));
        }

        MainActivity.ActivityResultReceived += OnResult;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                activity.StartIntentSenderForResult(
                    pendingIntent.IntentSender,
                    AuthorizationRequestCode,
                    null,
                    (ActivityFlags)0,
                    (ActivityFlags)0,
                    0));
            var result = await completion.Task.WaitAsync(cancellationToken);
            if (result.Data is null)
            {
                if (result.ResultCode == Result.Canceled)
                    throw new OperationCanceledException("Вход в Google отменён пользователем или Google Play Services не передал результат.");
                throw new InvalidOperationException("Google Play Services вернул пустой результат авторизации.");
            }

            return client.GetAuthorizationResultFromIntent(result.Data);
        }
        finally
        {
            MainActivity.ActivityResultReceived -= OnResult;
        }
    }

    private sealed record ActivityResult(Result ResultCode, Intent? Data);

    private static string BuildAuthorizationError(int statusCode) => statusCode switch
    {
        NetworkErrorStatusCode => "Google Play Services не смог подключиться к Google (код 7). " +
                                  "Проверьте доступ DONA CRM к сети и повторите вход.",
        InternalErrorStatusCode => "Google Play Services повторно вернул внутреннюю ошибку (код 8). " +
                                   "Повторите вход; если ошибка сохранится, перезапустите устройство.",
        DeveloperErrorStatusCode => "Android OAuth client настроен неверно (код 10). В Google Cloud package name должен быть " +
                                    "com.tnadevelopment.donacrm, а SHA-1 — соответствовать подписи установленного APK.",
        _ => $"Android не завершил авторизацию Google (код {statusCode}). Повторите попытку после проверки сети и Google Play Services."
    };
}
