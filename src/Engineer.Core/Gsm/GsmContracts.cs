using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Engineer.Core.Gsm;

public sealed record GsmProfileResult(JsonObject? Profile, IReadOnlyList<string> AvailableFuelTypes);
public sealed class GsmReportRejectedException(string message, bool invalidatesSession = false) : Exception(message)
{
    public bool InvalidatesSession { get; } = invalidatesSession;
}
public sealed record GsmProject(string Id, string Name, string BudgetCode)
{
    public override string ToString() => Name;
}
public sealed record GsmMonthData(string Month, IReadOnlyList<JsonObject> Routes, IReadOnlyList<JsonObject> Fuel, int? StartOdometer);
public sealed record GsmReportResult(string Month, bool Success, bool Generated, bool Sent, string? Status, string? OutputFileName, string? SentToEmail)
{
    public bool ConfirmsEmailDelivery => Sent || string.Equals(Status, "SENT", StringComparison.OrdinalIgnoreCase);
    public string Message => ConfirmsEmailDelivery
        ? $"{OutputFileName ?? "Excel-отчёт"} отправлен{(string.IsNullOrWhiteSpace(SentToEmail) ? "" : " на " + SentToEmail)}."
        : Generated ? $"{OutputFileName ?? "Excel-отчёт"} сформирован, но отправка письма не подтверждена."
        : "Отправка отчёта не подтверждена. Обновите историю перед повтором.";
}

public static class GsmValues
{
    public static string Text(JsonObject document, string key) => document[key] is JsonValue value
        ? value.TryGetValue<string>(out var text) ? text.Trim() : value.ToJsonString() : "";

    public static double? Number(JsonObject document, string key) => double.TryParse(Text(document, key).Replace(',', '.'),
        NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;

    public static bool IsValidMonth(string value) => Regex.IsMatch(value, @"^[0-9]{4}-(0[1-9]|1[0-2])$")
        && DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    public static bool IsValidDate(string value) => value.Length == 10 && DateOnly.TryParseExact(value,
        "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    // Keep the entire server document, including fields not shown by the editor.
    public static JsonObject NormalizeDocument(JsonObject document)
    {
        var result = (JsonObject)document.DeepClone();
        foreach (var pair in document)
        {
            var camel = Regex.Replace(pair.Key, "_([a-z])", match => match.Groups[1].Value.ToUpperInvariant());
            if (camel != pair.Key && !result.ContainsKey(camel)) result[camel] = pair.Value?.DeepClone();
        }
        foreach (var key in new[] { "fuelNorm", "defaultStartOdometer", "distanceKm", "periodStartOdometer", "reportedDistanceKm",
                     "reportedPeriodStartOdometer", "fuelLiters", "fuelCostRub", "liters", "fuelCost", "mileage" })
            if (result[key] != null && Number(result, key) is { } number)
                result[key] = number == Math.Truncate(number) && number >= int.MinValue && number <= int.MaxValue
                    ? JsonValue.Create((int)number) : JsonValue.Create(number);
        if (result["fuelTypes"] is not JsonArray { Count: > 0 } && Text(result, "fuelType") is { Length: > 0 } fuelType)
            result["fuelTypes"] = new JsonArray(fuelType);
        return result;
    }
}

public static class GsmProfileValidation
{
    public static IReadOnlyList<string> Validate(JsonObject profile, IReadOnlyList<string>? availableFuelTypes = null)
    {
        var errors = new List<string>();
        var required = new (string Key, string Name)[]
        {
            ("employeeFullName", "ФИО сотрудника"), ("employeeShortName", "Краткое имя сотрудника"),
            ("employeeReportSignName", "Подпись сотрудника в отчёте"), ("authorizedFullName", "ФИО уполномоченного"),
            ("authorizedShortName", "Краткое имя уполномоченного"), ("employeeJobTitle", "Должность"),
            ("employeeCompany", "Компания"), ("employeeAddress", "Адрес"), ("driverLicenseNumber", "Водительское удостоверение"),
            ("fuelCardNumber", "Топливная карта"), ("carModel", "Модель автомобиля"), ("licensePlate", "Госномер")
        };
        foreach (var field in required)
            if (GsmValues.Text(profile, field.Key).Length == 0) errors.Add($"Заполните поле «{field.Name}».");
        var digits = Regex.Replace(GsmValues.Text(profile, "employeePhone"), "[^0-9]", "");
        if (!(digits.Length == 10 || digits.Length == 11 && digits[0] is '7' or '8'))
            errors.Add("Введите телефон полностью: +7(XXX)XXX-XX-XX.");
        if (GsmValues.Text(profile, "posProjectId").Length == 0) errors.Add("Выберите проект POS из справочника.");
        if (GsmValues.Number(profile, "fuelNorm") is not > 0) errors.Add("Норма расхода топлива должна быть положительной.");
        var types = profile["fuelTypes"] is JsonArray array ? array.Select(x => x is JsonValue value && value.TryGetValue<string>(out var typeName) ? typeName : "").ToArray()
            : GsmValues.Text(profile, "fuelType") is { Length: > 0 } type ? new[] { type } : [];
        if (types.Length == 0 || types.Any(string.IsNullOrWhiteSpace)) errors.Add("Выберите хотя бы один тип топлива.");
        else if (availableFuelTypes != null && types.Any(type => !availableFuelTypes.Contains(type))) errors.Add("Выберите типы топлива из справочника сервера.");
        if (!GsmValues.IsValidMonth(GsmValues.Text(profile, "reportStartMonth"))) errors.Add("Начальный месяц отчёта должен быть в формате YYYY-MM.");
        if (profile["defaultStartOdometer"] != null && (GsmValues.Number(profile, "defaultStartOdometer") is not { } odometer
            || odometer < 0 || odometer != Math.Truncate(odometer))) errors.Add("Начальный одометр должен быть целым неотрицательным числом.");
        return errors;
    }
}
