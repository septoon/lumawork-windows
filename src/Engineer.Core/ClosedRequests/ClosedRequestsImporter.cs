using System.Globalization;
using System.Text.RegularExpressions;
using Engineer.Core.Spreadsheet;

namespace Engineer.Core.ClosedRequests;

public static class ClosedRequestsImporter
{
    private static readonly string[] Numbers = ["Номер заявки", "Номер", "number"];
    private static readonly string[] Statuses = ["Статус", "Состояние", "state", "status"];
    private static readonly string[] Addresses = ["Адрес установки терминала", "Адрес ТСП", "Адрес", "multicard_terminal_address"];
    private static readonly string[] ClosedDates = ["Выполнена", "Время Выполнена", "Время \"Выполнена\"", "Дата закрытия в МК", "Дата закрытия МК", "Дата закрытия", "completed_at", "closed_at", "resolved_at"];
    private static readonly string[] RegisteredDates = ["Время регистрации", "Дата регистрации", "Зарегистрирована", "registered_at", "opened_at", "sys_created_at"];

    public static ClosedRequestsImportPreview Parse(XlsxWorkbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            var rows = sheet.Cells.Values.Select(c => (Cell: c, Match: Regex.Match(c.Reference, "^([A-Za-z]+)([1-9][0-9]*)$")))
                .Where(c => c.Match.Success).GroupBy(c => int.Parse(c.Match.Groups[2].Value, CultureInfo.InvariantCulture)).OrderBy(g => g.Key).ToArray();
            Dictionary<string, string>? headers = null;
            var records = new List<ClosedRequest>();
            var rejected = 0;
            foreach (var row in rows)
            {
                var cells = row.ToDictionary(c => c.Match.Groups[1].Value.ToUpperInvariant(), c => c.Cell, StringComparer.Ordinal);
                if (headers is null)
                {
                    var candidate = cells.ToDictionary(c => c.Key, c => Normalize(c.Value.Text), StringComparer.Ordinal);
                    if (!candidate.Values.Any(v => Numbers.Any(n => Normalize(n) == v)) || !candidate.Values.Any(v => Statuses.Any(s => Normalize(s) == v))) continue;
                    headers = candidate;
                    continue;
                }
                if (cells.Values.All(c => string.IsNullOrWhiteSpace(c.Text) && c.Formula == null)) continue;
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                var invalidFormula = false;
                foreach (var (column, name) in headers)
                {
                    if (name.Length == 0 || !cells.TryGetValue(column, out var cell)) continue;
                    invalidFormula |= cell.Formula != null && !cell.HasCachedValue;
                    fields[name] = cell.Text.Trim();
                }
                var number = Get(fields, Numbers);
                var status = Get(fields, Statuses);
                if (invalidFormula || number.Length == 0 || !ClosedRequestsPolicy.IsTerminal(status)) { rejected++; continue; }
                var information = Get(fields, "Информация", "multicard_information");
                string ImportValue(params string[] keys)
                {
                    var direct = Get(fields, keys);
                    return direct.Length > 0 ? direct : InformationValue(information, keys);
                }
                var id = Get(fields, "sys_id", "Системный ID", "ID");
                records.Add(new(id.Length > 0 ? id : "number:" + number.ToUpperInvariant(), number, ImportValue(Addresses), status,
                    Date(ImportValue(ClosedDates), workbook.Date1904), Date(ImportValue(RegisteredDates), workbook.Date1904),
                    Date(Get(fields, "sys_updated_at", "Обновлено", "Дата обновления"), workbook.Date1904), Get(fields, "Тип заявки", "multicard_request_type", "request_type")));
            }
            if (headers != null)
            {
                var merged = ClosedRequestsMerge.Upsert(Array.Empty<ClosedRequest>(), records);
                return new(merged, rejected, merged.Count(r => r.Address.Length == 0));
            }
        }
        throw new InvalidDataException("В XLSX не найдены обязательные столбцы «Номер заявки» и «Статус».");
    }

    internal static string InformationValue(string information, params string[] keys)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in information.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            fields.TryAdd(Normalize(line[..colon]), line[(colon + 1)..].Trim());
        }
        return Get(fields, keys);
    }
    private static string Get(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(Normalize(key), out var value) && value.Length > 0) return value;
        return "";
    }
    private static string Normalize(string value) => Regex.Replace(value.Replace('\u00a0', ' '), @"\s+", " ").Trim().ToLowerInvariant().Replace('ё', 'е');
    private static string Date(string value, bool date1904)
    {
        if (value.Length == 0) return "";
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && double.IsFinite(serial))
        {
            try { return new DateTime(date1904 ? 1904 : 1899, date1904 ? 1 : 12, date1904 ? 1 : 30).AddDays(serial).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture); }
            catch (ArgumentOutOfRangeException) { return value; }
        }
        string[] formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "dd.MM.yyyy H:mm", "yyyy-MM-dd", "dd.MM.yyyy"];
        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : value;
    }
}
