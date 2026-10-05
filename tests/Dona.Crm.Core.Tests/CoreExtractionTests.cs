using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class GoogleSyncContractTests
{
    [Fact]
    public void Sync_status_distinguishes_local_pending_synced_conflict_and_error_states()
    {
        var successfulAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        var checkpoint = new GoogleSyncCheckpoint
        {
            LocalVersion = "local-v1",
            GoogleVersion = "remote-v1",
            LastSuccessfulAt = successfulAt
        };

        Assert.Equal(GoogleSyncState.LocalOnly, GoogleSyncStatusEvaluator.Evaluate(false, "local-v2", checkpoint).State);
        Assert.Equal(GoogleSyncState.Pending, GoogleSyncStatusEvaluator.Evaluate(true, "local-v2", checkpoint).State);

        var synced = GoogleSyncStatusEvaluator.Evaluate(true, "local-v1", checkpoint);
        Assert.Equal(GoogleSyncState.Synced, synced.State);
        Assert.Equal(successfulAt, synced.LastSuccessfulAt);

        checkpoint.HasConflict = true;
        checkpoint.LastError = "changed remotely";
        Assert.Equal(GoogleSyncState.Conflict, GoogleSyncStatusEvaluator.Evaluate(true, "local-v1", checkpoint).State);

        checkpoint.HasConflict = false;
        Assert.Equal(GoogleSyncState.Error, GoogleSyncStatusEvaluator.Evaluate(true, "local-v1", checkpoint).State);

        checkpoint.LastError = null;
        checkpoint.IsPending = true;
        Assert.Equal(GoogleSyncState.Pending, GoogleSyncStatusEvaluator.Evaluate(true, "local-v1", checkpoint).State);
    }

    [Fact]
    public void Fingerprint_is_stable_when_top_level_record_order_changes()
    {
        var first = new Product { Name = "Первый" };
        var second = new Product { Name = "Второй" };

        var ordered = new DonaSyncSnapshot { Products = [first, second] };
        var reversed = new DonaSyncSnapshot { Products = [second, first] };

        Assert.Equal(DonaSyncFingerprint.Create(ordered), DonaSyncFingerprint.Create(reversed));
    }
}

public sealed class BackupArchiveCodecTests
{
    [Fact]
    public void Creates_portable_archive_with_snapshot_and_local_images()
    {
        var bytes = BackupArchiveCodec.Create(
            new BackupSnapshot { Products = [new Product { Sku = "TS-1", Name = "Test" }] },
            [new BackupImage("photo.webp", [1, 2, 3])]);
        var inspection = BackupArchiveCodec.Inspect(bytes);

        Assert.Equal("TS-1", Assert.Single(inspection.Snapshot.Products).Sku);
        Assert.Equal(1, inspection.LocalImageCount);
        Assert.Equal(3, inspection.LocalImageBytes);
    }

    [Fact]
    public void Rejects_unsupported_backup_schema()
    {
        var bytes = BackupArchiveCodec.Create(new BackupSnapshot { SchemaVersion = 99 });

        var exception = Assert.Throws<InvalidOperationException>(() => BackupArchiveCodec.Inspect(bytes));

        Assert.Contains("не поддерживается", exception.Message);
    }

    [Fact]
    public void Rejects_duplicate_archive_paths()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("dona-crm-backup.json");
            archive.CreateEntry("DONA-CRM-BACKUP.JSON");
        }

        var exception = Assert.Throws<InvalidOperationException>(() => BackupArchiveCodec.Inspect(output.ToArray()));

        Assert.Contains("повторяющиеся пути", exception.Message);
    }

    [Fact]
    public void Creates_readable_json_export()
    {
        var export = BackupArchiveCodec.CreateJsonExport(
            new BackupSnapshot { Products = [new Product { Sku = "JSON-1", Name = "Export" }] });

        var snapshot = JsonSerializer.Deserialize<BackupSnapshot>(export.Content);

        Assert.Equal("application/json", export.ContentType);
        Assert.EndsWith(".json", export.FileName);
        Assert.Equal("JSON-1", Assert.Single(snapshot!.Products).Sku);
    }
}

public sealed class BusinessSettingsCompatibilityTests
{
    [Fact]
    public void Legacy_simple_interface_property_is_ignored_when_reading_settings()
    {
        const string legacyJson = """
            {
              "SimpleInterfaceMode": true,
              "LowStockThreshold": 7,
              "MainCurrencyCode": "UZS"
            }
            """;

        var settings = JsonSerializer.Deserialize<BusinessSettings>(legacyJson);

        Assert.NotNull(settings);
        Assert.Equal(7, settings.LowStockThreshold);
        Assert.Equal("UZS", settings.MainCurrencyCode);
        Assert.DoesNotContain("SimpleInterfaceMode", JsonSerializer.Serialize(settings));
    }
}
