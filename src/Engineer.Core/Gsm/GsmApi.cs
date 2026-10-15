using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engineer.Core.Auth;

namespace Engineer.Core.Gsm;

public sealed class GsmApi
{
    private readonly AuthTransport transport;
    public GsmApi(Uri origin, HttpClient? client = null) => transport = new(origin, client, false);

    public async Task<GsmProfileResult> FetchProfileAsync(string token, CancellationToken cancellationToken = default)
        => Profile(await Request("api/v2/gsm/profile", HttpMethod.Get, null, token, cancellationToken));

    public async Task<IReadOnlyList<GsmProject>> FetchProjectsAsync(string token, CancellationToken cancellationToken = default)
    {
        var response = await Request("api/v2/gsm/projects", HttpMethod.Get, null, token, cancellationToken);
        return Records(response, "projects").Select(row => new GsmProject(GsmValues.Text(row, "id"),
            GsmValues.Text(row, "name"), GsmValues.Text(row, "budgetCode")))
            .Where(project => project.Id.Length > 0 && project.Name.Length > 0 && project.BudgetCode.Length > 0).ToArray();
    }

    public async Task<GsmMonthData> FetchMonthAsync(string token, string month, CancellationToken cancellationToken = default)
    {
        RequireMonth(month);
        var first = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var last = new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month));
        var routeTask = Request($"api/v2/routes?from={first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", HttpMethod.Get, null, token, cancellationToken);
        var fuelTask = Request("api/v2/fuel", HttpMethod.Get, null, token, cancellationToken);
        var odometerTask = Request("api/v2/gsm/odometer-suggestion?month=" + month, HttpMethod.Get, null, token, cancellationToken);
        await Task.WhenAll(routeTask, fuelTask, odometerTask);
        var routes = RouteRecords(await routeTask).Where(row => InMonth(row, month)).OrderBy(row => GsmValues.Text(row, "date"), StringComparer.Ordinal).ToArray();
        var fuel = Records(await fuelTask, "records").Select(GsmValues.NormalizeDocument)
            .Where(row => InMonth(row, month) && string.Equals(GsmValues.Text(row, "recordType"), "fuel", StringComparison.OrdinalIgnoreCase)
                && (GsmValues.Number(row, "liters") is > 0 || GsmValues.Number(row, "fuelCost") is > 0)).ToArray();
        var suggestion = await odometerTask;
        if (GsmValues.Text(suggestion, "month") != month || !suggestion.ContainsKey("startOdometer")) throw InvalidResponse();
        int? start = null;
        if (suggestion["startOdometer"] != null)
        {
            if (GsmValues.Number(suggestion, "startOdometer") is not { } number || number < 0 || number > int.MaxValue || number != Math.Truncate(number)) throw InvalidResponse();
            start = (int)number;
        }
        return new(month, routes, fuel, start);
    }

    public async Task<GsmProfileResult> SaveProfileAsync(string token, JsonObject profile, CancellationToken cancellationToken = default)
    {
        var payload = GsmValues.NormalizeDocument(profile);
        var errors = GsmProfileValidation.Validate(payload);
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(profile));
        var digits = new string(GsmValues.Text(payload, "employeePhone").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 11) digits = digits[1..];
        payload["employeePhone"] = $"+7({digits[..3]}){digits[3..6]}-{digits[6..8]}-{digits[8..]}";
        if (payload["fuelTypes"] is JsonArray { Count: > 0 } types) payload["fuelType"] = types[0]?.DeepClone();
        return Profile(await Request("api/v2/gsm/profile", HttpMethod.Put, payload, token, cancellationToken));
    }

    public async Task<JsonObject> SaveRouteAsync(string token, JsonObject route, CancellationToken cancellationToken = default)
    {
        var payload = GsmValues.NormalizeDocument(route);
        var date = GsmValues.Text(payload, "date");
        if (!GsmValues.IsValidDate(date)) throw new ArgumentException("Укажите корректную дату маршрута.", nameof(route));
        if (GsmValues.Text(payload, "workType") is not ("POS" or "ARM") || payload["sent"] is not JsonValue sent
            || !sent.TryGetValue<bool>(out _) || payload["stops"] is not JsonArray)
            throw new ArgumentException("Для сохранения нужен полный маршрут с типом работ, статусом отправки и остановками. Обновите данные.", nameof(route));
        foreach (var key in new[] { "distanceKm", "periodStartOdometer", "reportedPeriodStartOdometer" })
            if (payload[key] != null && (GsmValues.Number(payload, key) is not { } number || number < 0 || number != Math.Truncate(number)))
                throw new ArgumentException("Пробег и одометр должны быть целыми неотрицательными числами.", nameof(route));
        return GsmValues.NormalizeDocument(await Request("api/v2/routes/" + date, HttpMethod.Put, payload, token, cancellationToken));
    }

    public async Task<JsonObject> SaveRefuelAsync(string token, JsonObject fuel, CancellationToken cancellationToken = default)
    {
        var payload = GsmValues.NormalizeDocument(fuel);
        if (GsmValues.Text(payload, "recordType") is { Length: > 0 } type && !string.Equals(type, "fuel", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("В этом разделе сохраняются только заправки.", nameof(fuel));
        if (!GsmValues.IsValidDate(GsmValues.Text(payload, "date")) || GsmValues.Number(payload, "liters") is not > 0
            || GsmValues.Number(payload, "fuelCost") is not >= 0 || GsmValues.Text(payload, "fuelType").Length == 0)
            throw new ArgumentException("Укажите дату, положительный объём, сумму и тип топлива заправки.", nameof(fuel));
        payload["recordType"] = "fuel";
        var id = GsmValues.Text(payload, "id");
        if (id.Length > 0 && !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) throw new ArgumentException("Некорректный идентификатор заправки.", nameof(fuel));
        return GsmValues.NormalizeDocument(await Request(id.Length == 0 ? "api/v2/fuel" : "api/v2/fuel/" + id,
            id.Length == 0 ? HttpMethod.Post : HttpMethod.Put, payload, token, cancellationToken));
    }

    public async Task<GsmReportResult> SendReportAsync(string token, string month, CancellationToken cancellationToken = default)
    {
        RequireMonth(month);
        JsonObject result;
        try { result = await Request("api/v2/gsm/report", HttpMethod.Post, new JsonObject { ["month"] = month }, token, cancellationToken); }
        catch (AuthException error) when (error.StatusCode == 401
            || error.StatusCode == 403 && error.ErrorCode == "USER_BLOCKED"
            || error.StatusCode == 400 && error.ErrorCode is "GSM_PROFILE_REQUIRED" or "VALIDATION_ERROR")
        {
            // These documented failures happen before a report job is created.
            var invalidatesSession = error.StatusCode is 401 or 403;
            var message = invalidatesSession ? error.Message : error.ErrorCode == "GSM_PROFILE_REQUIRED"
                ? "Сервер отклонил запрос до формирования отчёта. Проверьте обязательные поля и проекты ГСМ-профиля."
                : "Сервер отклонил параметры запроса до формирования отчёта. Проверьте выбранный месяц.";
            throw new GsmReportRejectedException(message, invalidatesSession);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            // Generation may continue after a timeout/cancellation. Never retry this POST automatically.
            throw new InvalidOperationException("Результат отправки неизвестен. Обновите историю отчётов перед повтором.", error);
        }
        if (GsmValues.Text(result, "month") != month || result["success"] is not JsonValue success || !success.TryGetValue<bool>(out var successValue)) throw InvalidResponse();
        return new(month, successValue, Flag(result, "generated"), Flag(result, "sent"), NullableText(result, "status"),
            NullableText(result, "outputFileName"), NullableText(result, "sentToEmail"));
    }

    public async Task<IReadOnlyList<JsonObject>> FetchJobsAsync(string token, CancellationToken cancellationToken = default)
        => Records(await Request("api/v2/gsm/jobs", HttpMethod.Get, null, token, cancellationToken), "jobs");

    private async Task<JsonObject> Request(string path, HttpMethod method, JsonObject? body, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new AuthException("Требуется сессия LumaWork.", true);
        var json = await transport.SendAsync(path, method, body, token, ct);
        return json.ValueKind == JsonValueKind.Object && JsonNode.Parse(json.GetRawText()) is JsonObject result ? result : throw InvalidResponse();
    }

    private static GsmProfileResult Profile(JsonObject response)
    {
        var available = response["availableFuelTypes"] is JsonArray array
            ? array.OfType<JsonValue>().Select(value => value.TryGetValue<string>(out var text) ? text : "").Where(text => text.Length > 0).ToArray() : [];
        if (response.ContainsKey("profile") && response["profile"] == null) return new(null, available);
        var raw = response["profile"] as JsonObject ?? response["gsmProfile"] as JsonObject ?? response["gsm_profile"] as JsonObject;
        if (raw == null && response.Any(pair => pair.Key.StartsWith("employee", StringComparison.Ordinal))) raw = response;
        return raw != null ? new(GsmValues.NormalizeDocument(raw), available) : throw InvalidResponse();
    }

    private static IReadOnlyList<JsonObject> Records(JsonObject response, string key)
        => response[key] is JsonArray array && array.All(item => item is JsonObject)
            ? array.Cast<JsonObject>().Select(item => (JsonObject)item.DeepClone()).ToArray() : throw InvalidResponse();

    private static IReadOnlyList<JsonObject> RouteRecords(JsonObject response)
    {
        IEnumerable<JsonObject> source;
        if (response["records"] is JsonArray) source = Records(response, "records");
        else if (response["days"] is JsonObject days)
            source = days.Select(pair =>
            {
                if (pair.Value is not JsonObject row) throw InvalidResponse();
                var copy = (JsonObject)row.DeepClone();
                if (!copy.ContainsKey("date")) copy["date"] = pair.Key.Split('|')[0];
                if (!copy.ContainsKey("workType")) copy["workType"] = pair.Key.EndsWith("|ARM", StringComparison.Ordinal) ? "ARM" : "POS";
                return copy;
            });
        else throw InvalidResponse();
        return source.Select(row =>
        {
            var normalized = GsmValues.NormalizeDocument(row);
            // Coordinates are stored in stop.payload on raw Prisma records.
            if (normalized["stops"] is JsonArray stops)
                foreach (var stop in stops.OfType<JsonObject>())
                    if (!stop.ContainsKey("coordinateOverride") && stop["payload"] is JsonObject payload && payload.ContainsKey("coordinateOverride"))
                        stop["coordinateOverride"] = payload["coordinateOverride"]?.DeepClone();
            return normalized;
        }).ToArray();
    }

    private static bool InMonth(JsonObject row, string month) => GsmValues.IsValidDate(GsmValues.Text(row, "date"))
        && GsmValues.Text(row, "date").StartsWith(month + "-", StringComparison.Ordinal);
    private static bool Flag(JsonObject value, string key) => value[key] is JsonValue flag && flag.TryGetValue<bool>(out var result) && result;
    private static string? NullableText(JsonObject value, string key) => GsmValues.Text(value, key) is { Length: > 0 } text ? text : null;
    private static void RequireMonth(string month) { if (!GsmValues.IsValidMonth(month)) throw new ArgumentException("Месяц должен быть в формате YYYY-MM.", nameof(month)); }
    private static InvalidDataException InvalidResponse() => new("Сервер вернул некорректные данные ГСМ. Обновите данные перед повтором.");
}
