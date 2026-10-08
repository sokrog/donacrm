using System.Text.Json;

namespace Dona.Crm.Web.Services;

/// <summary>Stores one versioned sync snapshot in the owner's hidden Drive appData folder.</summary>
public sealed class GoogleDriveSyncSnapshotClient(GoogleDriveFileClient drive)
{
    private const string FileName = "dona-crm-sync-v2.json";
    private const string ContentType = "application/json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GoogleSyncEnvelope> ReadAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var files = (await drive.ListAppDataAsync(FileName, accessToken, cancellationToken))
            .Where(file => string.Equals(file.Name, FileName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.ModifiedAt)
            .ToList();
        if (files.Count == 0) return Empty();
        if (files.Count > 1) throw new GoogleSyncConflictException("В личном облаке найдено несколько снимков синхронизации. Сохраните ZIP-копию и обратитесь в поддержку.");
        var metadata = await drive.GetMetadataAsync(files[0].Id, accessToken, cancellationToken);
        var download = await drive.DownloadAppDataAsync(metadata.Id, accessToken, cancellationToken);
        var envelope = JsonSerializer.Deserialize<GoogleSyncEnvelope>(download.Content, JsonOptions)
            ?? throw new InvalidDataException("Снимок синхронизации Google Drive повреждён.");
        envelope.Snapshot.ValidateFormat();
        return envelope with { RemoteId = metadata.Id, RemoteETag = metadata.ETag };
    }

    public async Task WriteAsync(string accessToken, GoogleSyncEnvelope envelope, CancellationToken cancellationToken = default)
    {
        envelope.Snapshot.ValidateFormat();
        var content = JsonSerializer.SerializeToUtf8Bytes(envelope with { RemoteId = null, RemoteETag = null }, JsonOptions);
        if (string.IsNullOrWhiteSpace(envelope.RemoteId))
        {
            await drive.UploadAppDataAsync(FileName, ContentType, content, accessToken, cancellationToken);
            return;
        }
        await drive.UpdateAppDataAsync(envelope.RemoteId, FileName, ContentType, content, accessToken, envelope.RemoteETag ?? string.Empty, cancellationToken);
    }

    private static GoogleSyncEnvelope Empty() => new(DonaSyncFingerprint.Create(new DonaSyncSnapshot()), DateTimeOffset.MinValue, new DonaSyncSnapshot());
}
