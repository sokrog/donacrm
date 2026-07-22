using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Dona.Crm.Web.Services;

public sealed class GoogleAtomicSyncPushService(GoogleMobileSyncSnapshotService snapshots)
{
    private const string JournalSheet = "SyncOperations";
    private static readonly string[] JournalHeaders = ["OperationId", "Status", "ExpectedGoogleVersion", "LocalVersion", "CreatedAt", "AppliedAt"];

    public async Task<GoogleSyncPushResult> PushAsync(GoogleSyncPushRequest request, string accessToken, CancellationToken cancellationToken)
    {
        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.SpreadsheetId))
            throw new InvalidOperationException("Операция синхронизации заполнена не полностью.");
        var actualLocalVersion = DonaSyncFingerprint.Create(request.Snapshot);
        if (!string.Equals(actualLocalVersion, request.LocalVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Локальный снимок изменён или повреждён во время передачи.");

        var current = await snapshots.CreateAsync(request.SpreadsheetId, accessToken, cancellationToken);
        if (string.Equals(current.Version, request.LocalVersion, StringComparison.Ordinal))
            return new(request.OperationId, request.LocalVersion, DateTimeOffset.UtcNow, true);
        if (!string.Equals(current.Version, request.ExpectedGoogleVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Google-таблица изменилась после сравнения. Обновите сравнение и проверьте данные ещё раз.");

        using var service = CreateService(accessToken);
        var mapped = GoogleSyncSheetMapper.Map(request.Snapshot);
        await EnsureSheetsAsync(service, request.SpreadsheetId, mapped.Select(value => value.Title).Append(JournalSheet), cancellationToken);

        var journal = await ReadJournalAsync(service, request.SpreadsheetId, cancellationToken);
        var applied = journal.FirstOrDefault(row => row.Count > 1 && string.Equals(row[0], request.OperationId.ToString(), StringComparison.OrdinalIgnoreCase) && string.Equals(row[1], "Applied", StringComparison.OrdinalIgnoreCase));
        if (applied is not null)
            return new(request.OperationId, request.LocalVersion, ParseDate(applied.ElementAtOrDefault(5)) ?? DateTimeOffset.UtcNow, true);

        current = await snapshots.CreateAsync(request.SpreadsheetId, accessToken, cancellationToken);
        if (!string.Equals(current.Version, request.ExpectedGoogleVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Google-таблица изменилась непосредственно перед отправкой. Данные не записаны.");

        var appliedAt = DateTimeOffset.UtcNow;
        journal.RemoveAll(row => row.Count > 0 && string.Equals(row[0], request.OperationId.ToString(), StringComparison.OrdinalIgnoreCase));
        journal.Add([request.OperationId.ToString(), "Applied", request.ExpectedGoogleVersion, request.LocalVersion, request.CreatedAt.ToString("O"), appliedAt.ToString("O")]);
        var sheets = mapped.Append(new GoogleSyncSheet(JournalSheet, JournalHeaders, journal.Select(row => (IReadOnlyList<object>)row.Cast<object>().ToList()).ToList())).ToList();
        var metadata = await service.Spreadsheets.Get(request.SpreadsheetId).ExecuteAsync(cancellationToken);
        var properties = metadata.Sheets.ToDictionary(value => value.Properties.Title, StringComparer.OrdinalIgnoreCase);
        var updates = sheets.Select(sheet => BuildUpdate(sheet, properties[sheet.Title].Properties)).ToList();
        await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = updates }, request.SpreadsheetId).ExecuteAsync(cancellationToken);
        return new(request.OperationId, request.LocalVersion, appliedAt, false);
    }

    public static Request BuildUpdate(GoogleSyncSheet sheet, SheetProperties properties)
    {
        var rows = new List<RowData> { ToRow(sheet.Headers.Cast<object>()) };
        rows.AddRange(sheet.Rows.Select(ToRow));
        return new Request
        {
            UpdateCells = new UpdateCellsRequest
            {
                Range = new GridRange
                {
                    SheetId = properties.SheetId,
                    StartRowIndex = 0,
                    EndRowIndex = Math.Max(properties.GridProperties?.RowCount ?? rows.Count, rows.Count),
                    StartColumnIndex = 0,
                    EndColumnIndex = sheet.Headers.Count
                },
                Rows = rows,
                Fields = "userEnteredValue"
            }
        };
    }

    private static RowData ToRow(IEnumerable<object> values) => new() { Values = values.Select(value => new CellData { UserEnteredValue = ToValue(value) }).ToList() };

    private static ExtendedValue ToValue(object value) => value switch
    {
        bool boolean => new ExtendedValue { BoolValue = boolean },
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => new ExtendedValue { NumberValue = Convert.ToDouble(value, CultureInfo.InvariantCulture) },
        _ => new ExtendedValue { StringValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty }
    };

    private static async Task EnsureSheetsAsync(SheetsService service, string spreadsheetId, IEnumerable<string> titles, CancellationToken cancellationToken)
    {
        var metadata = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync(cancellationToken);
        var existing = metadata.Sheets.Select(value => value.Properties.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = titles.Distinct(StringComparer.OrdinalIgnoreCase).Where(title => !existing.Contains(title)).ToList();
        if (missing.Count == 0) return;
        var requests = missing.Select(title => new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = title } } }).ToList();
        await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = requests }, spreadsheetId).ExecuteAsync(cancellationToken);
    }

    private static async Task<List<List<string>>> ReadJournalAsync(SheetsService service, string spreadsheetId, CancellationToken cancellationToken)
    {
        var values = (await service.Spreadsheets.Values.Get(spreadsheetId, $"{JournalSheet}!A2:F").ExecuteAsync(cancellationToken)).Values ?? [];
        return values.Select(row => row.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).ToList()).ToList();
    }

    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? result : null;

    private static SheetsService CreateService(string accessToken) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
        ApplicationName = "Dona CRM Mobile Sync"
    });
}
