using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Dona.Crm.Web.Services;

public sealed class GoogleSheetsSnapshotClient(HttpClient http)
{
    private const string SnapshotSheetTitle = "_CommandOrbitSync";
    private const int ChunkLength = 40_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GoogleSyncEnvelope> ReadAsync(
        string spreadsheetId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var range = Uri.EscapeDataString($"'{SnapshotSheetTitle}'!A:C");
        using var response = await SendAsync(
            HttpMethod.Get,
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{range}",
            accessToken,
            null,
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            return await ReadVisibleSheetsAsync(spreadsheetId, accessToken, cancellationToken);

        await EnsureSuccessAsync(response, "Не удалось прочитать данные синхронизации", cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("values", out var values) || values.GetArrayLength() == 0)
            return await ReadVisibleSheetsAsync(spreadsheetId, accessToken, cancellationToken);

        var rows = values.EnumerateArray().ToArray();
        if (rows[0].GetArrayLength() < 3 || rows[0][0].GetString() != "CommandOrbitSyncV1")
            throw new InvalidOperationException("Скрытый лист синхронизации имеет неизвестный формат.");

        var version = rows[0][1].GetString() ?? string.Empty;
        var capturedAt = DateTimeOffset.Parse(
            rows[0][2].GetString() ?? string.Empty,
            System.Globalization.CultureInfo.InvariantCulture);
        var encoded = string.Concat(
            rows.Skip(1)
                .OrderBy(row => int.Parse(row[0].GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture))
                .Select(row => row[1].GetString()));
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var snapshot = JsonSerializer.Deserialize<DonaSyncSnapshot>(json, JsonOptions)
            ?? throw new InvalidOperationException("Google Sheets вернул пустой снимок данных.");
        return new(version, capturedAt, snapshot);
    }

    public async Task WriteAsync(
        string spreadsheetId,
        string accessToken,
        GoogleSyncEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        await EnsureSnapshotSheetAsync(spreadsheetId, accessToken, cancellationToken);
        var range = Uri.EscapeDataString($"'{SnapshotSheetTitle}'!A:C");
        using (var clear = await SendAsync(
                   HttpMethod.Post,
                   $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{range}:clear",
                   accessToken,
                   new { },
                   cancellationToken))
        {
            await EnsureSuccessAsync(clear, "Не удалось очистить предыдущий снимок", cancellationToken);
        }

        var encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope.Snapshot, JsonOptions)));
        var rows = new List<object[]>
        {
            new object[] { "CommandOrbitSyncV1", envelope.Version, envelope.CapturedAt.ToString("O") }
        };
        for (var offset = 0; offset < encoded.Length; offset += ChunkLength)
        {
            rows.Add(
            [
                (offset / ChunkLength + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                encoded.Substring(offset, Math.Min(ChunkLength, encoded.Length - offset))
            ]);
        }

        var writeRange = Uri.EscapeDataString($"'{SnapshotSheetTitle}'!A1");
        using var write = await SendAsync(
            HttpMethod.Put,
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{writeRange}?valueInputOption=RAW",
            accessToken,
            new
            {
                range = $"'{SnapshotSheetTitle}'!A1",
                majorDimension = "ROWS",
                values = rows
            },
            cancellationToken);
        await EnsureSuccessAsync(write, "Не удалось записать снимок", cancellationToken);
    }

    private async Task<GoogleSyncEnvelope> ReadVisibleSheetsAsync(
        string spreadsheetId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var escapedSpreadsheetId = Uri.EscapeDataString(spreadsheetId);
        using var metadataResponse = await SendAsync(
            HttpMethod.Get,
            $"https://sheets.googleapis.com/v4/spreadsheets/{escapedSpreadsheetId}?fields=sheets.properties.title",
            accessToken,
            null,
            cancellationToken);
        await EnsureSuccessAsync(metadataResponse, "Не удалось получить список листов Google-таблицы", cancellationToken);

        using var metadata = JsonDocument.Parse(await metadataResponse.Content.ReadAsStringAsync(cancellationToken));
        var existingTitles = metadata.RootElement.GetProperty("sheets")
            .EnumerateArray()
            .Select(sheet => sheet.GetProperty("properties").GetProperty("title").GetString())
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var titles = GoogleSheetSnapshotParser.SheetTitles.Where(existingTitles.Contains).ToArray();
        if (titles.Length == 0)
            return EmptyEnvelope();

        var ranges = string.Join(
            "&",
            titles.Select(title => $"ranges={Uri.EscapeDataString($"'{title}'!A:ZZ")}"));
        using var valuesResponse = await SendAsync(
            HttpMethod.Get,
            $"https://sheets.googleapis.com/v4/spreadsheets/{escapedSpreadsheetId}/values:batchGet?majorDimension=ROWS&valueRenderOption=UNFORMATTED_VALUE&dateTimeRenderOption=FORMATTED_STRING&{ranges}",
            accessToken,
            null,
            cancellationToken);
        await EnsureSuccessAsync(valuesResponse, "Не удалось прочитать бизнес-листы Google-таблицы", cancellationToken);

        using var valuesDocument = JsonDocument.Parse(await valuesResponse.Content.ReadAsStringAsync(cancellationToken));
        var valueRanges = valuesDocument.RootElement.TryGetProperty("valueRanges", out var rangesElement)
            ? rangesElement.EnumerateArray().ToArray()
            : [];
        var sheets = new List<GoogleSyncSheet>();
        for (var index = 0; index < titles.Length && index < valueRanges.Length; index++)
        {
            if (!valueRanges[index].TryGetProperty("values", out var rowsElement) || rowsElement.GetArrayLength() == 0)
                continue;

            var rows = rowsElement.EnumerateArray().ToArray();
            var headers = rows[0].EnumerateArray()
                .Select(CellValue)
                .Select(value => value.ToString() ?? string.Empty)
                .ToArray();
            var dataRows = rows.Skip(1)
                .Select(row => (IReadOnlyList<object>)row.EnumerateArray().Select(CellValue).ToArray())
                .ToArray();
            sheets.Add(new GoogleSyncSheet(titles[index], headers, dataRows));
        }

        if (sheets.Count == 0)
            return EmptyEnvelope();

        var import = GoogleSheetSnapshotParser.Parse(sheets);
        if (!import.IsValid)
        {
            var details = string.Join(
                "; ",
                import.Issues.Take(5).Select(issue => $"{issue.Sheet}, строка {issue.Row}: {issue.Message}"));
            var suffix = import.Issues.Count > 5 ? $" Ещё ошибок: {import.Issues.Count - 5}." : string.Empty;
            throw new InvalidOperationException($"В существующей Google-таблице найдены ошибки: {details}.{suffix}");
        }

        return new(DonaSyncFingerprint.Create(import.Snapshot), DateTimeOffset.UtcNow, import.Snapshot);
    }

    private async Task EnsureSnapshotSheetAsync(
        string spreadsheetId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var range = Uri.EscapeDataString($"'{SnapshotSheetTitle}'!A1");
        using var probe = await SendAsync(
            HttpMethod.Get,
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{range}",
            accessToken,
            null,
            cancellationToken);
        if (probe.IsSuccessStatusCode)
            return;

        using var create = await SendAsync(
            HttpMethod.Post,
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}:batchUpdate",
            accessToken,
            new
            {
                requests = new[]
                {
                    new { addSheet = new { properties = new { title = SnapshotSheetTitle, hidden = true } } }
                }
            },
            cancellationToken);
        await EnsureSuccessAsync(create, "Не удалось создать служебный лист синхронизации", cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string accessToken,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string message,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"{message}: {detail}");
    }

    private static object CellValue(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.String => cell.GetString() ?? string.Empty,
        JsonValueKind.Number when cell.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when cell.TryGetDecimal(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => cell.ToString()
    };

    private static GoogleSyncEnvelope EmptyEnvelope()
    {
        var snapshot = new DonaSyncSnapshot();
        return new(DonaSyncFingerprint.Create(snapshot), DateTimeOffset.MinValue, snapshot);
    }
}
