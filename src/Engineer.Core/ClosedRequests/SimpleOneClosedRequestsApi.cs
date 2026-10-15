using System.Globalization;
using System.Text.Json;
using Engineer.Core.Auth;

namespace Engineer.Core.ClosedRequests;

public sealed record SimpleOneClosedRequestsQuery(string CurrentUserDynamicId, string AssignedUserDynamicId, string ResolvedDateOptionId);

public sealed class SimpleOneClosedRequestsApi
{
    private readonly AuthTransport transport;
    private readonly SimpleOneClosedRequestsQuery query;
    private const int PageSize = 100;
    private const string Columns = "sys_id,number,state,multicard_terminal_address,multicard_information,multicard_additional_information,additional_information,description,registered_at,opened_at,sys_created_at,sys_updated_at,completed_at,closed_at,multicard_closing_date,resolved_at,multicard_request_type,itsm_request_type";

    public SimpleOneClosedRequestsApi(Uri origin, HttpClient? client = null, SimpleOneClosedRequestsQuery? query = null)
    {
        transport = new(origin, client, true);
        this.query = query ?? new(Environment.GetEnvironmentVariable("SIMPLEONE_CURRENT_USER_DYNAMIC_ID") ?? "",
            Environment.GetEnvironmentVariable("SIMPLEONE_ASSIGNED_USER_DYNAMIC_ID") ?? "",
            Environment.GetEnvironmentVariable("SIMPLEONE_RESOLVED_DATE_OPTION_ID") ?? "");
    }

    // A complete server snapshot is staged in memory. The caller commits/merges only after success.
    public async Task<IReadOnlyList<ClosedRequest>> FetchAsync(string userId, string authKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || !userId.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("Некорректный пользователь SimpleOne.", nameof(userId));
        if (string.IsNullOrWhiteSpace(authKey)) throw new ArgumentException("Требуется сессия SimpleOne.", nameof(authKey));
        foreach (var id in new[] { query.CurrentUserDynamicId, query.AssignedUserDynamicId, query.ResolvedDateOptionId })
            if (string.IsNullOrEmpty(id) || !id.All(c => c is >= '0' and <= '9'))
                throw new InvalidOperationException("Не настроены фильтры закрытых заявок SimpleOne.");
        var condition = $"((multicard_engineerDYNAMIC{query.CurrentUserDynamicId}^ORassigned_userDYNAMIC{query.AssignedUserDynamicId}^ORengineer_schedule.employeeDYNAMIC{query.CurrentUserDynamicId})^resolved_atNOTONopt:{query.ResolvedDateOptionId}^stateNOT INon_hold@assigned@in_progress@escalated@returned_to_work@3@6@update_received)^ORDERBYDESCsys_updated_at^ORDERBYDESCsys_id";
        var staged = new Dictionary<string, ClosedRequest>(StringComparer.Ordinal);
        int? expectedTotal = null;
        for (var page = 1; page <= 10_000; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = $"list/itsm_request?condition={Uri.EscapeDataString(condition)}&page={page}&per_page={PageSize}&columns={Uri.EscapeDataString(Columns)}";
            var response = await transport.SendAsync(path, HttpMethod.Get, null, authKey, cancellationToken);
            var items = ListItems(response);
            var total = Total(response);
            if (total.HasValue)
            {
                if (expectedTotal.HasValue && expectedTotal != total) throw Incomplete();
                expectedTotal = total;
            }
            if (items.Length > PageSize) throw Incomplete();
            var previousCount = staged.Count;
            foreach (var item in items)
            {
                if (item.ValueKind != JsonValueKind.Object) throw Incomplete();
                var record = Map(item);
                if (string.IsNullOrEmpty(record.Id) || string.IsNullOrEmpty(record.Number)) throw Incomplete();
                if (staged.TryGetValue(record.Id, out var previous)
                    && string.CompareOrdinal(previous.UpdatedAt, record.UpdatedAt) > 0) continue;
                staged[record.Id] = record;
            }
            if (expectedTotal.HasValue && staged.Count > expectedTotal.Value) throw Incomplete();
            if (page > 1 && items.Length > 0 && staged.Count == previousCount) throw Incomplete();
            if (expectedTotal == staged.Count || (!expectedTotal.HasValue && items.Length < PageSize))
                return ClosedRequestsMerge.Upsert(Array.Empty<ClosedRequest>(), staged.Values);
            // Empty/short pages with a remaining advertised total and repeated pages cannot be a complete archive.
            if (items.Length < PageSize || staged.Count == previousCount) throw Incomplete();
        }
        throw Incomplete();
    }

    private static ClosedRequest Map(JsonElement item)
    {
        var information = string.Join('\n', new[] { Field(item, "multicard_information", "information", "Информация"),
            Field(item, "multicard_additional_information", "additional_information"), Field(item, "description") });
        var address = Field(item, "multicard_terminal_address", "Адрес установки терминала", "Адрес ТСП", "Адрес");
        if (address.Length == 0) address = ClosedRequestsImporter.InformationValue(information, "Адрес установки терминала", "Адрес ТСП", "Адрес");
        return new(Field(item, "sys_id", "id"), Field(item, "number", "__display_value"), address,
            Field(item, true, "state"), Field(item, "completed_at", "closed_at", "multicard_closing_date", "resolved_at"),
            Field(item, "registered_at", "opened_at", "sys_created_at"), Field(item, "sys_updated_at"),
            Field(item, "multicard_request_type", "itsm_request_type", "request_type"));
    }

    private static string Field(JsonElement item, params string[] keys) => Field(item, false, keys);
    private static string Field(JsonElement item, bool display, params string[] keys)
    {
        foreach (var key in keys)
            if (item.TryGetProperty(key, out var value) && Value(value, display) is { Length: > 0 } text) return text;
        return "";
    }
    private static string Value(JsonElement value, bool display)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString()?.Trim() ?? "";
        if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in display ? new[] { "display_value", "value", "database_value" } : new[] { "value", "display_value", "database_value" })
                if (value.TryGetProperty(key, out var nested) && Value(nested, display) is { Length: > 0 } text) return text;
        }
        return "";
    }
    private static JsonElement[] ListItems(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object) throw Incomplete();
        foreach (var container in Containers(response).Take(2))
        {
            foreach (var key in new[] { "items", "list", "records" })
                if (container.TryGetProperty(key, out var value))
                    return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : throw Incomplete();
            if (container.TryGetProperty("item", out var item))
                return item.ValueKind == JsonValueKind.Object ? new[] { item } : throw Incomplete();
        }
        throw Incomplete();
    }
    private static IEnumerable<JsonElement> Containers(JsonElement response)
    {
        var hasData = response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object;
        yield return hasData ? data : response;
        yield return response;
        foreach (var parent in hasData ? new[] { response, data } : new[] { response })
            foreach (var key in new[] { "meta", "pagination" })
                if (parent.TryGetProperty(key, out var nested) && nested.ValueKind == JsonValueKind.Object) yield return nested;
    }
    private static int? Total(JsonElement response)
    {
        int? result = null;
        foreach (var container in Containers(response))
            foreach (var key in new[] { "total", "total_count", "totalCount", "records_total", "recordsTotal" })
                if (container.TryGetProperty(key, out var value))
                {
                    if (!int.TryParse(Value(value, false), NumberStyles.None, CultureInfo.InvariantCulture, out var total) || total < 0
                        || (result.HasValue && result != total)) throw Incomplete();
                    result = total;
                }
        return result;
    }
    private static InvalidDataException Incomplete() => new("SimpleOne вернул неполный или изменившийся список. Сохранённый архив не изменён; повторите обновление.");
}
