using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Services;

namespace Dona.Crm.App.Services;

public sealed class MauiGoogleSyncService(
    HttpClient http,
    IGoogleConnectionService connection,
    IGoogleAccessTokenProvider tokens,
    SqliteSyncStore local) : IGoogleSyncService
{
    public async Task<GoogleSyncPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var remote = await GetRemoteAsync(cancellationToken);
        var localSnapshot = await local.ReadAsync(cancellationToken);
        return Preview(localSnapshot, remote);
    }

    public async Task<GoogleSyncPreview> PullAsync(string expectedGoogleVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedGoogleVersion)) throw new InvalidOperationException("Сначала обновите предварительный просмотр.");
        var remote = await GetRemoteAsync(cancellationToken);
        if (!string.Equals(remote.Version, expectedGoogleVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Google-таблица изменилась после предварительного просмотра. Обновите сравнение и проверьте данные ещё раз.");
        await local.ReplaceAsync(remote.Snapshot, cancellationToken);
        var applied = await local.ReadAsync(cancellationToken);
        return Preview(applied, remote);
    }

    private async Task<GoogleSyncEnvelope> GetRemoteAsync(CancellationToken cancellationToken)
    {
        var state = await connection.GetStateAsync(cancellationToken);
        if (!state.IsConnected || !state.IsConfigured) throw new InvalidOperationException("Сначала подключите и проверьте Google в разделе «Подключения».");
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{state.Settings.BrokerBaseUrl}/api/mobile/sync/snapshot?spreadsheetId={Uri.EscapeDataString(state.Settings.SpreadsheetId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var json = JsonDocument.Parse(detail);
                detail = json.RootElement.TryGetProperty("detail", out var value) ? value.GetString() ?? detail : detail;
            }
            catch (JsonException) { }
            throw new InvalidOperationException($"Не удалось получить снимок Google Sheets: {detail}");
        }
        return await response.Content.ReadFromJsonAsync<GoogleSyncEnvelope>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Сервер вернул пустой снимок Google Sheets.");
    }

    private static GoogleSyncPreview Preview(DonaSyncSnapshot localSnapshot, GoogleSyncEnvelope remote) => new(
        DonaSyncFingerprint.Create(localSnapshot),
        remote.Version,
        remote.CapturedAt,
        DonaSyncFingerprint.Compare(localSnapshot, remote.Snapshot));
}
