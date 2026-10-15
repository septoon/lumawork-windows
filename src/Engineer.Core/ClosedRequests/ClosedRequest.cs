namespace Engineer.Core.ClosedRequests;

// Only the Lite projection is persisted; information/description and contact data are never retained.
public sealed record ClosedRequest(string Id, string Number, string Address, string Status,
    string ClosedAt, string RegisteredAt, string UpdatedAt, string RequestType);

public sealed record ClosedRequestsImportPreview(IReadOnlyList<ClosedRequest> Records, int RejectedCount, int MissingAddressCount);

public static class ClosedRequestsMerge
{
    public static IReadOnlyList<ClosedRequest> Upsert(IEnumerable<ClosedRequest> existing, IEnumerable<ClosedRequest> incoming)
    {
        var byNumber = new Dictionary<string, ClosedRequest>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in existing.Concat(incoming))
        {
            if (string.IsNullOrWhiteSpace(record.Number) || string.IsNullOrWhiteSpace(record.Id) || !ClosedRequestsPolicy.IsTerminal(record.Status)) continue;
            byNumber[record.Number] = record;
        }
        return byNumber.Values.OrderByDescending(r => r.Number, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal static class ClosedRequestsPolicy
{
    internal static bool IsTerminal(string status)
    {
        status = status.Trim().ToLowerInvariant();
        return status is "closed" or "completed" or "resolved"
            || new[] { "выполн", "закрыт", "решен", "решён", "отказ", "отклон" }.Any(status.Contains);
    }
}
