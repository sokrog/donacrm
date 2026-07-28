using Dona.Crm.Web.Services;

namespace Dona.Crm.App.Services;

public sealed class MauiBackupArchiveFileService : IBackupArchiveFileService
{
    private const int AutomaticBackupLimit = 7;

    public async Task<byte[]?> PickAsync(CancellationToken cancellationToken = default)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Выберите ZIP-копию DONA CRM",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["application/zip"],
                [DevicePlatform.WinUI] = [".zip"],
                [DevicePlatform.iOS] = ["public.zip-archive"],
                [DevicePlatform.MacCatalyst] = ["public.zip-archive"]
            })
        });
        if (result is null) return null;
        await using var input = await result.OpenReadAsync();
        using var output = new MemoryStream();
        await input.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }

    public async Task SaveAutomaticBackupAsync(BackupDownload backup, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(FileSystem.AppDataDirectory, "automatic-backups");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, backup.FileName), backup.Content, cancellationToken);
        foreach (var obsolete in Directory.EnumerateFiles(directory, "*.zip")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(AutomaticBackupLimit))
            File.Delete(obsolete);
    }

    public async Task ExportAsync(BackupDownload backup, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(FileSystem.CacheDirectory, "exports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(backup.FileName));
        await File.WriteAllBytesAsync(path, backup.Content, cancellationToken);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = backup.ContentType == "application/zip" ? "Сохранить ZIP-копию DONA CRM" : "Сохранить данные DONA CRM",
            File = new ShareFile(path, backup.ContentType)
        });
    }
}
