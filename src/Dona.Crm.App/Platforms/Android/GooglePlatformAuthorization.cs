using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;

namespace Dona.Crm.App.Services;

public sealed class GooglePlatformAuthorization : IGooglePlatformAuthorization
{
    private const int AuthorizationRequestCode = 40721;
    private static readonly string[] ScopeNames =
    [
        "openid",
        "email",
        "https://www.googleapis.com/auth/spreadsheets",
        "https://www.googleapis.com/auth/drive"
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
        var result = await client.Authorize(request).AsAsync<AuthorizationResult>();
        if (result.HasResolution)
        {
            if (!interactive)
                throw new InvalidOperationException("Сессия Google завершена. Выполните вход ещё раз.");
            result = await ResolveAsync(activity, client, result, cancellationToken);
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

    private static async Task<AuthorizationResult> ResolveAsync(
        MainActivity activity,
        IAuthorizationClient client,
        AuthorizationResult initial,
        CancellationToken cancellationToken)
    {
        var pendingIntent = initial.PendingIntent
            ?? throw new InvalidOperationException("Google Play Services не вернул окно подтверждения.");
        var completion = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnResult(int requestCode, Result resultCode, Intent? data)
        {
            if (requestCode != AuthorizationRequestCode)
                return;
            if (resultCode == Result.Ok)
                completion.TrySetResult(data);
            else
                completion.TrySetException(new InvalidOperationException("Подключение Google отменено."));
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
            var data = await completion.Task.WaitAsync(cancellationToken)
                ?? throw new InvalidOperationException("Google Play Services вернул пустой результат.");
            return client.GetAuthorizationResultFromIntent(data);
        }
        finally
        {
            MainActivity.ActivityResultReceived -= OnResult;
        }
    }
}
