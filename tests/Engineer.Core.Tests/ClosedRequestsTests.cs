using System.Text.Json;
using Engineer.Core.Auth;
using Engineer.Core.ClosedRequests;
using Engineer.Core.Spreadsheet;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class ClosedRequestsTests
{
    [Fact]
    public void HeaderImportUsesInformationFallbackAndLastRowWinsWithoutPersistingDescription()
    {
        var preview = ClosedRequestsImporter.Parse(Workbook(
            ("A3", " Статус "), ("C3", "Номер\u00a0заявки"), ("E3", "Информация"), ("F3", "Адрес"), ("G3", "Выполнена"),
            ("A4", "Закрыта"), ("C4", "REQ001"), ("E4", "Адрес ТСП: Старый адрес\nТелефон: PRIVATE_VALUE"),
            ("A5", "Выполнена"), ("C5", "REQ001"), ("E5", "Адрес ТСП: Адрес: с двоеточием\nТелефон: PRIVATE_VALUE"), ("G5", "2026-10-14 12:30:00")));
        var record = Assert.Single(preview.Records);
        Assert.Equal("REQ001", record.Number);
        Assert.Equal("Адрес: с двоеточием", record.Address);
        Assert.Equal("14.10.2026 12:30", record.ClosedAt);
        Assert.Equal(0, preview.RejectedCount);
        Assert.Equal(0, preview.MissingAddressCount);
        Assert.DoesNotContain("PRIVATE_VALUE", JsonSerializer.Serialize(record));
        Assert.Equal(record.Id, Assert.Single(ClosedRequestsImporter.Parse(Workbook(("A1", "Номер заявки"), ("B1", "Статус"), ("A2", "REQ001"), ("B2", "closed"))).Records).Id);
    }

    [Theory]
    [InlineData("closed", true)] [InlineData("completed", true)] [InlineData("resolved", true)]
    [InlineData("Выполнена", true)] [InlineData("Закрыта", true)] [InlineData("Решён", true)]
    [InlineData("Отказ", true)] [InlineData("Отклонена", true)]
    [InlineData("assigned", false)] [InlineData("", false)] [InlineData("in_progress", false)]
    public void ImportRequiresTerminalStatusEvenForReturnEquipment(string status, bool accepted)
    {
        var preview = ClosedRequestsImporter.Parse(Workbook(("A1", "Номер заявки"), ("B1", "Статус"), ("C1", "Тип заявки"),
            ("A2", "REQ001"), ("B2", status), ("C2", "returnEquip")));
        Assert.Equal(accepted ? 1 : 0, preview.Records.Count);
        Assert.Equal(accepted ? 0 : 1, preview.RejectedCount);
        Assert.Equal(accepted ? 1 : 0, preview.MissingAddressCount);
    }

    [Fact]
    public void MissingStatusHeaderDoesNotAssumeClosed()
    {
        Assert.Throws<InvalidDataException>(() => ClosedRequestsImporter.Parse(Workbook(("A1", "Номер заявки"), ("A2", "REQ001"))));
    }

    [Fact]
    public void ImportPrefersDirectAddressAndHonorsExcelDateSystem()
    {
        var workbook = Workbook(("A1", "Номер заявки"), ("B1", "Статус"), ("C1", "Адрес"), ("D1", "Информация"), ("E1", "Дата закрытия"),
            ("A2", "REQ002"), ("B2", "completed"), ("C2", "Прямой адрес"), ("D2", "Адрес: Неверный"), ("E2", "1.5"));
        var record = Assert.Single(ClosedRequestsImporter.Parse(new(workbook.Sheets, workbook.CustomProperties, workbook.SourceHash, true)).Records);
        Assert.Equal("Прямой адрес", record.Address);
        Assert.Equal("02.01.1904 12:00", record.ClosedAt);
    }

    [Fact]
    public void MergeUpsertsByNumberPreservesAbsentRecordsAndRejectsOpenRows()
    {
        var old = new[] { Request("old", "REQ001", "Старый"), Request("keep", "REQ002", "Сохранить") };
        var merged = ClosedRequestsMerge.Upsert(old, new[] { Request("sys-1", "REQ001", "Новый"), Request("sys-3", "REQ003", "Не включать") with { Status = "assigned", RequestType = "returnEquip" } });
        Assert.Equal(2, merged.Count);
        Assert.Equal("Новый", merged.Single(r => r.Number == "REQ001").Address);
        Assert.Equal("sys-1", merged.Single(r => r.Number == "REQ001").Id);
        Assert.Contains(merged, r => r.Number == "REQ002");
        Assert.Equal("Старый", old[0].Address);
    }

    [Fact]
    public async Task SimpleOneUsesPersonalClosedQueryCookieAndDisplayValues()
    {
        using var http = AuthContractTests.Client(r =>
        {
            Assert.Equal("/v1/list/itsm_request", r.RequestUri!.AbsolutePath);
            Assert.Equal("auth=synthetic-so-key", Assert.Single(r.Headers.GetValues("Cookie")));
            Assert.Null(r.Headers.Authorization);
            var query = Uri.UnescapeDataString(r.RequestUri.Query);
            Assert.Contains("((multicard_engineerDYNAMIC11^ORassigned_userDYNAMIC22^ORengineer_schedule.employeeDYNAMIC11)^resolved_atNOTONopt:33^stateNOT INon_hold@assigned@in_progress@escalated@returned_to_work@3@6@update_received)^ORDERBYDESCsys_updated_at^ORDERBYDESCsys_id", query);
            Assert.Contains("per_page=100", query);
            Assert.DoesNotContain("sys_updated_at>", query);
            return Task.FromResult(AuthContractTests.Response(200, """{"status":"OK","data":{"items":[{"sys_id":"id-1","number":"REQ001","state":{"value":"completed","display_value":"Выполнена"},"multicard_terminal_address":{"value":"Улица 1"},"completed_at":"2026-10-14 12:00:00","sys_updated_at":"2026-10-15 01:00:00","multicard_information":"Телефон: PRIVATE_VALUE"},{"sys_id":"id-2","number":"REQ002","state":"assigned","multicard_request_type":"returnEquip"}],"total":2}}"""));
        });
        var record = Assert.Single(await Api(http).FetchAsync("user-1", "synthetic-so-key"));
        Assert.Equal("id-1", record.Id);
        Assert.Equal("Улица 1", record.Address);
        Assert.Equal("Выполнена", record.Status);
        Assert.Equal("2026-10-14 12:00:00", record.ClosedAt);
        Assert.Equal("2026-10-15 01:00:00", record.UpdatedAt);
        Assert.DoesNotContain("PRIVATE_VALUE", JsonSerializer.Serialize(record));
    }

    [Fact]
    public async Task SimpleOneLoadsAllPagesBeforePublishingSnapshot()
    {
        var calls = 0;
        using var http = AuthContractTests.Client(r =>
        {
            calls++;
            Assert.Contains("page=" + calls, r.RequestUri!.Query);
            var rows = calls == 1 ? Enumerable.Range(1, 100).Select(i => new { sys_id = "id-" + i, number = "REQ" + i, state = "closed", multicard_terminal_address = "Адрес" })
                : new[] { new { sys_id = "id-101", number = "REQ101", state = "closed", multicard_terminal_address = "Новый" } };
            return Task.FromResult(AuthContractTests.Response(200, JsonSerializer.Serialize(new { status = "OK", data = new { items = rows, total = 101 } })));
        });
        Assert.Equal(101, (await Api(http).FetchAsync("user", "key")).Count);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":{\"items\":[],\"total\":1}}")]
    [InlineData("{\"data\":{\"items\":[{\"number\":\"REQ001\",\"state\":\"closed\"}]}}")]
    [InlineData("{\"data\":{\"items\":[{\"sys_id\":\"id\",\"state\":\"closed\"}]}}")]
    [InlineData("{\"data\":{\"items\":[],\"total\":\"garbage\"}}")]
    public async Task MalformedOrIncompleteSnapshotFailsWithoutReturningPartialList(string body)
    {
        using var http = AuthContractTests.Client(_ => Task.FromResult(AuthContractTests.Response(200, body)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Api(http).FetchAsync("user", "key"));
    }

    [Fact]
    public async Task RepeatedPageIsRejected()
    {
        var calls = 0;
        var rows = Enumerable.Range(1, 100).Select(i => new { sys_id = "id-" + i, number = "REQ" + i, state = "closed" });
        using var http = AuthContractTests.Client(_ => { calls++; return Task.FromResult(AuthContractTests.Response(200, JsonSerializer.Serialize(new { data = new { items = rows, total = 201 } }))); });
        await Assert.ThrowsAsync<InvalidDataException>(() => Api(http).FetchAsync("user", "key"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ShortRepeatedPageWithoutTotalCannotPublishIncompleteSnapshot()
    {
        var calls = 0;
        using var http = AuthContractTests.Client(_ =>
        {
            var count = ++calls == 1 ? 100 : 1;
            var rows = Enumerable.Range(1, count).Select(i => new { sys_id = "id-" + i, number = "REQ" + i, state = "closed" });
            return Task.FromResult(AuthContractTests.Response(200, JsonSerializer.Serialize(new { data = new { items = rows } })));
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Api(http).FetchAsync("user", "key"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailedSecondPageCannotReplaceExistingArchive()
    {
        IReadOnlyList<ClosedRequest> archive = new[] { Request("keep", "REQ-OLD", "Сохранить") };
        var rows = Enumerable.Range(1, 100).Select(i => new { sys_id = "id-" + i, number = "REQ" + i, state = "closed" });
        var calls = 0;
        using var http = AuthContractTests.Client(_ => Task.FromResult(++calls == 1
            ? AuthContractTests.Response(200, JsonSerializer.Serialize(new { data = new { items = rows, total = 101 } }))
            : AuthContractTests.Response(401, "{}")));
        await Assert.ThrowsAsync<AuthException>(async () => archive = ClosedRequestsMerge.Upsert(archive, await Api(http).FetchAsync("user", "key")));
        Assert.Equal("REQ-OLD", Assert.Single(archive).Number);
    }

    [Fact]
    public async Task SessionCookiesDoNotLeakBetweenRefreshesAndInformationOnlySuppliesAddress()
    {
        var seen = new List<string>();
        using var http = AuthContractTests.Client(r =>
        {
            seen.Add(Assert.Single(r.Headers.GetValues("Cookie")));
            return Task.FromResult(AuthContractTests.Response(200, """{"data":{"items":[{"sys_id":"id","number":"REQ1","state":"closed","description":"Адрес ТСП: Улица 7\nТелефон: PRIVATE_VALUE"}],"total":1}}"""));
        });
        var api = Api(http);
        var first = Assert.Single(await api.FetchAsync("first-user", "first-key"));
        var second = Assert.Single(await api.FetchAsync("second-user", "second-key"));
        Assert.Equal(new[] { "auth=first-key", "auth=second-key" }, seen);
        Assert.Equal("Улица 7", first.Address);
        Assert.Equal("Улица 7", second.Address);
        Assert.DoesNotContain("PRIVATE_VALUE", JsonSerializer.Serialize(first));
    }

    [Fact]
    public async Task CancelledRefreshDoesNotIssueRequest()
    {
        using var http = AuthContractTests.Client(_ => throw new Xunit.Sdk.XunitException("Unexpected request"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Api(http).FetchAsync("user", "key", cancellation.Token));
    }

    [Fact]
    public async Task InvalidPersonalIdIsRejectedBeforeSending()
    {
        using var http = AuthContractTests.Client(_ => throw new Xunit.Sdk.XunitException("Unexpected request"));
        await Assert.ThrowsAsync<ArgumentException>(() => Api(http).FetchAsync("user^ORassigned_user=other", "key"));
    }

    private static SimpleOneClosedRequestsApi Api(HttpClient http) => new(new Uri("https://so.example.invalid/v1"), http, new("11", "22", "33"));
    private static ClosedRequest Request(string id, string number, string address) => new(id, number, address, "closed", "", "", "", "");
    private static XlsxWorkbook Workbook(params (string Reference, string Text)[] values) => new(new[] { new XlsxSheet("Закрытые", values.ToDictionary(v => v.Reference, v => new XlsxCell(v.Reference, v.Text))) }, new Dictionary<string, string>(), "synthetic");
}
