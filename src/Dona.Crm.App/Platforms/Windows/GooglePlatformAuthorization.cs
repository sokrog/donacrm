using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Dona.Crm.App.Services;

public sealed class GooglePlatformAuthorization(
    HttpClient http,
    IConfiguration configuration) : IGooglePlatformAuthorization
{
    private const string WindowsClientIdConfigPath = "GoogleOAuth:WindowsClientId";
    private const string WindowsClientSecretConfigPath = "GoogleOAuth:WindowsClientSecret";

    public bool RequiresClientId => false;

    public async Task<GooglePlatformToken> AuthorizeAsync(
        string? clientId,
        string? clientSecret,
        bool interactive,
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        clientId = RequireClientId(configuration[WindowsClientIdConfigPath]);
        clientSecret = configuration[WindowsClientSecretConfigPath];
        if (!interactive)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidOperationException("Сессия Google завершена. Выполните вход ещё раз.");
            return await GoogleOAuthPkce.RefreshAsync(http, clientId, clientSecret, refreshToken, cancellationToken);
        }

        var port = ReserveLoopbackPort();
        var redirectUri = new Uri($"http://127.0.0.1:{port}/oauth2redirect/");
        var verifier = GoogleOAuthPkce.CreateVerifier();
        var state = GoogleOAuthPkce.CreateState();
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri.ToString());
        listener.Start();

        await Browser.Default.OpenAsync(
            GoogleOAuthPkce.AuthorizationUri(clientId, redirectUri, verifier, state),
            BrowserLaunchMode.SystemPreferred);

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("Подключение Google отменено.");
        }

        var query = context.Request.QueryString;
        await ReplyAsync(
            context.Response,
            string.IsNullOrWhiteSpace(query["error"])
                ? "DONA CRM подключена к Google. Можно закрыть эту вкладку."
                : "Подключение Google не завершено. Вернитесь в DONA CRM.");
        if (!string.Equals(query["state"], state, StringComparison.Ordinal))
            throw new InvalidOperationException("Google OAuth вернул неверный state.");
        if (!string.IsNullOrWhiteSpace(query["error"]))
            throw new InvalidOperationException($"Google OAuth: {query["error"]}.");
        var code = query["code"];
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Google OAuth не вернул код авторизации.");

        return await GoogleOAuthPkce.ExchangeAsync(
            http,
            clientId,
            clientSecret,
            redirectUri,
            code,
            verifier,
            cancellationToken);
    }

    public async Task DisconnectAsync(string? accessToken, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
            await GoogleOAuthPkce.RevokeAsync(http, accessToken, cancellationToken);
    }

    private static string RequireClientId(string? clientId) =>
        !string.IsNullOrWhiteSpace(clientId) &&
        clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)
            ? clientId.Trim()
            : throw new InvalidOperationException(
                $"Задайте '{WindowsClientIdConfigPath}' в appsettings для OAuth-клиента Google типа Desktop app.");

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task ReplyAsync(HttpListenerResponse response, string message)
    {
        var content = Encoding.UTF8.GetBytes(
            $"<!doctype html><html lang=\"ru\"><meta charset=\"utf-8\"><title>DONA CRM</title><body style=\"font:18px system-ui;padding:40px;background:#0b0711;color:#fff\">{WebUtility.HtmlEncode(message)}</body></html>");
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = content.Length;
        await response.OutputStream.WriteAsync(content);
        response.Close();
    }
}
