using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public enum PersonalBackupKind
{
    Manual,
    Automatic
}

public sealed record PersonalBackupInfo(
    string Id,
    string FileName,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    PersonalBackupKind Kind);

/// <summary>
/// Keeps recovery archives in the hidden appData folder of the signed-in Google account.
/// The account owner, not DONA CRM infrastructure, owns the storage.
/// </summary>
public interface IPersonalCloudBackupService
{
    Task<IReadOnlyList<PersonalBackupInfo>> ListAsync(CancellationToken cancellationToken = default);
    Task<PersonalBackupInfo> CreateAsync(CancellationToken cancellationToken = default);
    Task<PersonalBackupInfo> SaveAutomaticAsync(BackupDownload backup, CancellationToken cancellationToken = default);
    Task<BackupRestoreResult> RestoreAsync(string backupId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string backupId, CancellationToken cancellationToken = default);
}

public sealed class GoogleDrivePersonalBackupService(
    IGoogleConnectionService connection,
    IGoogleAccessTokenProvider tokens,
    GoogleDriveFileClient drive,
    IBackupSnapshotStore store,
    BackupRestoreService restore) : IPersonalCloudBackupService
{
    private const string ArchivePrefix = "dona-crm-personal-";
    private const string ManualArchivePrefix = "dona-crm-personal-manual-";
    private const string AutomaticArchivePrefix = "dona-crm-personal-auto-";
    private const int AutomaticBackupLimit = 7;

    public async Task<IReadOnlyList<PersonalBackupInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        var files = await drive.ListAppDataAsync(ArchivePrefix, await tokens.GetAccessTokenAsync(cancellationToken), cancellationToken);
        return files
            .Where(file => file.Name.StartsWith(ArchivePrefix, StringComparison.Ordinal) && file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Select(ToPersonalBackup)
            .OrderByDescending(file => file.CreatedAt)
            .ToList();
    }

    public async Task<PersonalBackupInfo> CreateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        return await UploadAsync(
            ManualArchivePrefix,
            BackupArchiveCodec.Create(BackupSnapshotMapper.FromSyncSnapshot(snapshot)),
            PersonalBackupKind.Manual,
            cancellationToken);
    }

    public async Task<PersonalBackupInfo> SaveAutomaticAsync(BackupDownload backup, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        var uploaded = await UploadAsync(AutomaticArchivePrefix, backup.Content, PersonalBackupKind.Automatic, cancellationToken);
        await RemoveExpiredAutomaticBackupsAsync(cancellationToken);
        return uploaded;
    }

    public async Task<BackupRestoreResult> RestoreAsync(string backupId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await EnsurePersonalBackupAsync(backupId, cancellationToken);
        var archive = await drive.DownloadAppDataAsync(backupId, await tokens.GetAccessTokenAsync(cancellationToken), cancellationToken);
        if (!string.Equals(archive.ContentType, "application/zip", StringComparison.OrdinalIgnoreCase) && archive.Content.Length == 0)
            throw new InvalidDataException("Google Drive вернул пустую резервную копию.");
        return await restore.RestoreAsync(archive.Content, cancellationToken);
    }

    public async Task DeleteAsync(string backupId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await EnsurePersonalBackupAsync(backupId, cancellationToken);
        await drive.DeleteAsync(backupId, await tokens.GetAccessTokenAsync(cancellationToken), cancellationToken);
    }

    private async Task<PersonalBackupInfo> UploadAsync(
        string prefix,
        byte[] content,
        PersonalBackupKind kind,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var file = await drive.UploadAppDataAsync(
            $"{prefix}{now:yyyyMMdd-HHmmss-fff}.zip",
            "application/zip",
            content,
            await tokens.GetAccessTokenAsync(cancellationToken),
            cancellationToken);
        return new PersonalBackupInfo(file.Id, file.Name, file.ModifiedAt ?? now, file.Size, kind);
    }

    private async Task RemoveExpiredAutomaticBackupsAsync(CancellationToken cancellationToken)
    {
        var automaticBackups = (await ListAsync(cancellationToken))
            .Where(backup => backup.Kind == PersonalBackupKind.Automatic)
            .OrderByDescending(backup => backup.CreatedAt)
            .Skip(AutomaticBackupLimit)
            .ToList();
        var accessToken = automaticBackups.Count == 0 ? null : await tokens.GetAccessTokenAsync(cancellationToken);
        foreach (var backup in automaticBackups)
            await drive.DeleteAsync(backup.Id, accessToken!, cancellationToken);
    }

    private async Task EnsurePersonalBackupAsync(string backupId, CancellationToken cancellationToken)
    {
        var backup = (await ListAsync(cancellationToken)).FirstOrDefault(item => item.Id == backupId);
        if (backup is null)
            throw new InvalidOperationException("Личная копия не найдена или недоступна текущему Google-аккаунту.");
    }

    private static PersonalBackupInfo ToPersonalBackup(GoogleDriveFile file) => new(
        file.Id,
        file.Name,
        file.ModifiedAt ?? DateTimeOffset.MinValue,
        file.Size,
        file.Name.StartsWith(AutomaticArchivePrefix, StringComparison.Ordinal)
            ? PersonalBackupKind.Automatic
            : PersonalBackupKind.Manual);

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (!(await connection.GetStateAsync(cancellationToken)).IsConnected)
            throw new InvalidOperationException("Сначала войдите в Google в разделе «Подключения».");
    }
}
