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
}
