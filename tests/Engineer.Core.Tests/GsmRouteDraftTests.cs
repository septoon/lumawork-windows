using System.Text.Json.Nodes;
using Engineer.Core.Gsm;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class GsmRouteDraftTests
{
    [Fact]
    public void UnrelatedStatusEditPreservesReportOverridesCoordinatesAndHiddenFields()
    {
        var original = Route();
        var snapshot = original.ToJsonString();
        var draft = GsmRouteDraft.Apply(original, "2026-10-01", "ARM", 25, 1000,
            [("Address A", "R1", "Отказ"), ("Address B", "R2", "В процессе")]);
        Assert.Equal("custom route", GsmValues.Text(draft, "routeSummary"));
        Assert.Equal("legacy route", GsmValues.Text(draft, "route"));
        Assert.Equal("custom requests", GsmValues.Text(draft, "requestNumbersSummary"));
        Assert.Equal("legacy requests", GsmValues.Text(draft, "request_numbers"));
        Assert.Equal(40, GsmValues.Number(draft, "reportedDistanceKm"));
        Assert.Equal(900, GsmValues.Number(draft, "reportedPeriodStartOdometer"));
        Assert.True(draft["sent"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(original["unknown"], draft["unknown"]));
        var stop = draft["stops"]![0]!.AsObject();
        Assert.Equal("Отказ", GsmValues.Text(stop, "status"));
        foreach (var key in new[] { "id", "org", "tid", "reason", "rejectReason", "declineReason", "payload", "coordinateOverride", "hidden" })
            Assert.True(JsonNode.DeepEquals(original["stops"]![0]![key], stop[key]), key);
        Assert.Equal(snapshot, original.ToJsonString());
        stop["payload"]!["hidden"] = "changed in draft";
        Assert.Equal(snapshot, original.ToJsonString());
    }

    [Fact]
    public void AddressEditClearsBothRouteAliasesAndBothCoordinateSources()
    {
        var original = Route();
        var draft = GsmRouteDraft.Apply(original, "2026-10-01", "ARM", 25, 1000,
            [("Changed A", "R1", "Выполнена"), ("Address B", "R2", "В процессе")]);
        Assert.Null(draft["routeSummary"]);
        Assert.Null(draft["route"]);
        Assert.Equal("custom requests", GsmValues.Text(draft, "requestNumbersSummary"));
        var changed = draft["stops"]![0]!.AsObject();
        Assert.Null(changed["coordinateOverride"]);
        Assert.Null(changed["payload"]!["coordinateOverride"]);
        Assert.Equal("keep", changed["payload"]!["hidden"]!.GetValue<string>());
        Assert.Equal("org-a", GsmValues.Text(changed, "org"));
        Assert.NotNull(draft["stops"]![1]!["coordinateOverride"]);
        Assert.NotNull(original["stops"]![0]!["coordinateOverride"]);
    }

    [Fact]
    public void RequestNumberEditClearsBothRequestAliasesButRetainsRouteOverrideAndCoordinates()
    {
        var original = Route();
        var draft = GsmRouteDraft.Apply(original, "2026-10-01", "ARM", 25, 1000,
            [("Address A", "R3", "Выполнена"), ("Address B", "R2", "В процессе")]);
        Assert.Null(draft["requestNumbersSummary"]);
        Assert.Null(draft["request_numbers"]);
        Assert.Equal("custom route", GsmValues.Text(draft, "routeSummary"));
        Assert.NotNull(draft["stops"]![0]!["coordinateOverride"]);
    }

    [Fact]
    public void ChangedDistanceAndOdometerReplaceOnlyTheirReportedOverrides()
    {
        var draft = GsmRouteDraft.Apply(Route(), "2026-10-01", "ARM", 30, 1100,
            [("Address A", "R1", "Выполнена"), ("Address B", "R2", "В процессе")]);
        Assert.Equal(30, GsmValues.Number(draft, "distanceKm"));
        Assert.Equal(30, GsmValues.Number(draft, "reportedDistanceKm"));
        Assert.Equal(1100, GsmValues.Number(draft, "periodStartOdometer"));
        Assert.Equal(1100, GsmValues.Number(draft, "reportedPeriodStartOdometer"));
        Assert.Equal("custom route", GsmValues.Text(draft, "routeSummary"));
    }

    [Fact]
    public void ClearingDistanceAndOdometerAlsoClearsTheirReportedOverrides()
    {
        var draft = GsmRouteDraft.Apply(Route(), "2026-10-01", "ARM", null, null,
            [("Address A", "R1", "Выполнена"), ("Address B", "R2", "В процессе")]);
        foreach (var key in new[] { "distanceKm", "reportedDistanceKm", "periodStartOdometer", "reportedPeriodStartOdometer" }) Assert.Null(draft[key]);
    }

    [Fact]
    public void AddedStopGetsNewIdentityAndBlankMetadataWithoutBorrowingCoordinates()
    {
        var draft = GsmRouteDraft.Apply(Route(), "2026-10-01", "ARM", 25, 1000,
            [("Address A", "R1", "Выполнена"), ("Address B", "R2", "В процессе"), ("Address C", "R3", "Отказ")]);
        var stop = draft["stops"]![2]!.AsObject();
        Assert.True(Guid.TryParse(GsmValues.Text(stop, "id"), out _));
        Assert.Equal("Address C", GsmValues.Text(stop, "address"));
        Assert.Equal("R3", GsmValues.Text(stop, "requestNumber"));
        Assert.Equal("Отказ", GsmValues.Text(stop, "status"));
        foreach (var key in new[] { "org", "tid", "reason", "rejectReason" }) Assert.Equal("", stop[key]!.GetValue<string>());
        Assert.Null(stop["coordinateOverride"]);
        Assert.Null(draft["routeSummary"]);
        Assert.Null(draft["requestNumbersSummary"]);
    }

    [Fact]
    public void RemovingStopsClearsBothSummaries()
    {
        var draft = GsmRouteDraft.Apply(Route(), "2026-10-01", "ARM", 25, 1000, []);
        Assert.Empty(draft["stops"]!.AsArray());
        Assert.Null(draft["routeSummary"]);
        Assert.Null(draft["route"]);
        Assert.Null(draft["requestNumbersSummary"]);
        Assert.Null(draft["request_numbers"]);
    }

    [Fact]
    public void NewRouteGetsProvidedKeyNumbersAndUnsentState()
    {
        var draft = GsmRouteDraft.Apply(new JsonObject(), "2028-02-29", "POS", 15, 500,
            [("Address A", "R1", "В процессе")]);
        Assert.Equal("2028-02-29", GsmValues.Text(draft, "date"));
        Assert.Equal("POS", GsmValues.Text(draft, "workType"));
        Assert.False(draft["sent"]!.GetValue<bool>());
        Assert.Equal(15, GsmValues.Number(draft, "reportedDistanceKm"));
        Assert.Equal(500, GsmValues.Number(draft, "reportedPeriodStartOdometer"));
    }

    [Theory]
    [InlineData("2026-10-02", "ARM")]
    [InlineData("2026-10-01", "POS")]
    public void ExistingRouteCannotBeMovedToAnotherKey(string date, string workType)
        => Assert.Throws<ArgumentException>(() => GsmRouteDraft.Apply(Route(), date, workType, 25, 1000, []));

    [Theory]
    [InlineData("2026-02-29", "POS", 1, 1)]
    [InlineData("2026-10-01", "OTHER", 1, 1)]
    [InlineData("2026-10-01", "POS", -1, 1)]
    [InlineData("2026-10-01", "POS", 1, -1)]
    public void InvalidNewDraftIsRejected(string date, string workType, int distance, int odometer)
        => Assert.Throws<ArgumentException>(() => GsmRouteDraft.Apply(new JsonObject(), date, workType, distance, odometer, []));

    private static JsonObject Route() => JsonNode.Parse("""{"date":"2026-10-01","workType":"ARM","distanceKm":25,"periodStartOdometer":1000,"reportedDistanceKm":40,"reportedPeriodStartOdometer":900,"routeSummary":"custom route","route":"legacy route","requestNumbersSummary":"custom requests","request_numbers":"legacy requests","sent":true,"unknown":{"flag":true},"stops":[{"id":"stop-a","address":"Address A","requestNumber":"R1","status":"Выполнена","org":"org-a","tid":"tid-a","reason":"reason-a","rejectReason":"reject-a","declineReason":"decline-a","hidden":[1,2],"coordinateOverride":{"latitude":45,"longitude":35},"payload":{"coordinateOverride":{"latitude":45,"longitude":35},"hidden":"keep"}},{"id":"stop-b","address":"Address B","requestNumber":"R2","status":"В процессе","coordinateOverride":{"latitude":46,"longitude":36}}]}""")!.AsObject();
}
