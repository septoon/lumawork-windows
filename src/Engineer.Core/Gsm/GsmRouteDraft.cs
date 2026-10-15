using System.Text.Json.Nodes;

namespace Engineer.Core.Gsm;

public static class GsmRouteDraft
{
    public static JsonObject Apply(JsonObject original, string date, string workType, int? distanceKm, int? periodStartOdometer,
        IReadOnlyList<(string Address, string RequestNumber, string Status)> editedStops)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(editedStops);
        if (!GsmValues.IsValidDate(date)) throw new ArgumentException("Укажите корректную дату маршрута.", nameof(date));
        if (workType is not ("POS" or "ARM")) throw new ArgumentException("Выберите тип работ POS или ARM.", nameof(workType));
        if (distanceKm < 0) throw new ArgumentException("Пробег не может быть отрицательным.", nameof(distanceKm));
        if (periodStartOdometer < 0) throw new ArgumentException("Начальный одометр не может быть отрицательным.", nameof(periodStartOdometer));
        var isNew = original.Count == 0;
        if (!isNew && (GsmValues.Text(original, "date") != date || GsmValues.Text(original, "workType") != workType))
            throw new ArgumentException("Дата и тип работ существующего маршрута не могут изменяться.", nameof(original));
        if (!isNew && (original["sent"] is not JsonValue sent || !sent.TryGetValue<bool>(out _)
            || original["stops"] is not JsonArray))
            throw new ArgumentException("Маршрут загружен не полностью. Обновите данные перед изменением.", nameof(original));

        var previousStops = original["stops"] as JsonArray ?? [];
        if (previousStops.Any(stop => stop is not JsonObject))
            throw new ArgumentException("Сервер вернул некорректные остановки маршрута.", nameof(original));
        var edits = editedStops.Select(stop => (Address: stop.Address.Trim(), RequestNumber: stop.RequestNumber.Trim(), Status: stop.Status.Trim())).ToArray();
        var addressesChanged = !previousStops.Cast<JsonObject>().Select(stop => GsmValues.Text(stop, "address"))
            .SequenceEqual(edits.Select(stop => stop.Address), StringComparer.Ordinal);
        var requestsChanged = !previousStops.Cast<JsonObject>().Select(stop => GsmValues.Text(stop, "requestNumber"))
            .SequenceEqual(edits.Select(stop => stop.RequestNumber), StringComparer.Ordinal);
        var draft = (JsonObject)original.DeepClone();
        draft["date"] = date;
        draft["workType"] = workType;
        if (isNew) draft["sent"] = false;
        draft["distanceKm"] = distanceKm;
        if (GsmValues.Number(original, "distanceKm") != distanceKm) draft["reportedDistanceKm"] = distanceKm;
        draft["periodStartOdometer"] = periodStartOdometer;
        if (GsmValues.Number(original, "periodStartOdometer") != periodStartOdometer) draft["reportedPeriodStartOdometer"] = periodStartOdometer;
        if (addressesChanged) { draft["routeSummary"] = null; draft["route"] = null; }
        if (requestsChanged) { draft["requestNumbersSummary"] = null; draft["request_numbers"] = null; }

        var stops = new JsonArray();
        for (var index = 0; index < edits.Length; index++)
        {
            var edit = edits[index];
            var previous = index < previousStops.Count ? previousStops[index]!.AsObject() : null;
            var stop = previous == null ? new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(), ["org"] = "", ["tid"] = "", ["reason"] = "", ["rejectReason"] = ""
            } : (JsonObject)previous.DeepClone();
            if (previous != null && GsmValues.Text(previous, "address") != edit.Address)
            {
                stop["coordinateOverride"] = null;
                if (stop["payload"] is JsonObject payload) payload["coordinateOverride"] = null;
            }
            stop["address"] = edit.Address;
            stop["requestNumber"] = edit.RequestNumber;
            stop["status"] = edit.Status;
            stops.Add(stop);
        }
        draft["stops"] = stops;
        return draft;
    }
}
