using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engineer.Core.Auth;
using Engineer.Core.ClosedRequests;
using Engineer.Core.Gsm;
using Engineer.Core.Workspace;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public async Task SameSessionDoesNotRefreshTwiceAndOfflineReloadKeepsUnknownFieldsWithoutSecrets()
    {
        var store = new MemoryStore(); var calls = 0;
        using var http = Client(r => { calls++; return Task.FromResult(ReadResponse(r)); });
        var controller = Create(http, store);
        await controller.SetSessionAsync(App("a"), null, true);
        var firstCalls = calls;
        Assert.NotNull(controller.LastGsmUpdated);
        await controller.SetSessionAsync(App("a"), null, true);
        Assert.Equal(firstCalls, calls);
        var offline = Create(http, store);
        await offline.SetSessionAsync(App("a"), null, false);
        Assert.Equal("keep", offline.ProfileResult.Profile!["future"]!["value"]!.GetValue<string>());
        Assert.Equal(firstCalls, calls);
        var cached = string.Join("", store.Documents.Values.Select(v => v.ToJsonString()));
        Assert.DoesNotContain("secret-token", cached);
        Assert.DoesNotContain("PRIVATE_COOKIE", cached);
        Assert.DoesNotContain("PRIVATE_PASSWORD", cached);
    }

    [Fact]
    public async Task CachedDataPublishesBeforeNetworkCompletes()
    {
        var store = new MemoryStore();
        using var good = Client(r => Task.FromResult(ReadResponse(r)));
        await Create(good, store).SetSessionAsync(App("a"), null, true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var delayed = Client(async r => { entered.TrySetResult(); await release.Task; return ReadResponse(r); });
        var next = Create(delayed, store);
        var loading = next.SetSessionAsync(App("a"), null, true);
        await entered.Task;
        Assert.NotNull(next.ProfileResult.Profile);
        Assert.True(next.IsBusy);
        release.SetResult(); await loading;
    }

    [Fact]
    public async Task LateAccountResponseDoesNotPublishOrPersistIntoAnotherAccount()
    {
        var store = new MemoryStore();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Client(async r => { entered.TrySetResult(); await release.Task; return ReadResponse(r); });
        var controller = Create(http, store);
        var loading = controller.SetSessionAsync(App("a"), null, true);
        await entered.Task;
        await controller.SetSessionAsync(App("b"), null, false);
        release.SetResult(); await loading;
        Assert.Null(controller.ProfileResult.Profile);
        Assert.Empty(store.Documents);
        Assert.Null(controller.ErrorMessage);
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public async Task ManualAndEachSimpleOneAccountHaveSeparateArchives()
    {
        var store = new MemoryStore(); using var http = Client(r => Task.FromResult(ReadResponse(r)));
        var controller = Create(http, store);
        await controller.SetSessionAsync(App("a"), null, false);
        Assert.True(await controller.ImportClosedAsync(Preview("MANUAL")));
        await controller.SetSessionAsync(App("a"), So("manual"), false);
        Assert.Empty(controller.ClosedRequests); // Scope discriminator is separate from user-provided IDs.
        await controller.SetSessionAsync(App("a"), So("s1"), false);
        Assert.Empty(controller.ClosedRequests);
        Assert.True(await controller.ImportClosedAsync(Preview("SO1")));
        await controller.SetSessionAsync(App("a"), So("s2"), false);
        Assert.Empty(controller.ClosedRequests);
        await controller.SetSessionAsync(App("a"), So("s1"), false);
        Assert.Equal("SO1", Assert.Single(controller.ClosedRequests).Number);
        await controller.SetSessionAsync(App("a"), null, false);
        Assert.Equal("MANUAL", Assert.Single(controller.ClosedRequests).Number);
        await controller.SetSessionAsync(App("b"), null, false);
        Assert.Empty(controller.ClosedRequests);
    }

    [Fact]
    public async Task FailedArchivePersistenceDoesNotPublishImport()
    {
        var store = new MemoryStore(); using var http = Client(r => Task.FromResult(ReadResponse(r)));
        var controller = Create(http, store);
        await controller.SetSessionAsync(App("a"), null, false);
        Assert.True(await controller.ImportClosedAsync(Preview("OLD")));
        store.FailWrites = true;
        Assert.False(await controller.ImportClosedAsync(Preview("NEW")));
        Assert.Equal("OLD", Assert.Single(controller.ClosedRequests).Number);
        Assert.NotNull(controller.ErrorMessage);
    }

    [Fact]
    public async Task SuccessfulMutationNeedsReadbackAndIsNeverRetriedAfterReadbackFailure()
    {
        var puts = 0; var failReads = false;
        using var http = Client(r =>
        {
            if (r.Method == HttpMethod.Put) { puts++; return Task.FromResult(Response("{}")); }
            if (failReads) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return Task.FromResult(ReadResponse(r));
        });
        var controller = Create(http, new MemoryStore());
        await controller.SetSessionAsync(App("a"), null, true);
        failReads = true;
        Assert.False(await controller.SaveRouteAsync(new JsonObject { ["date"] = controller.Month + "-01", ["workType"] = "POS", ["sent"] = false, ["stops"] = new JsonArray() }));
        Assert.Equal(1, puts);
        Assert.Contains("Обновите", controller.ErrorMessage);
    }

    [Fact]
    public async Task ReportMarkerMustPersistBeforeSendingAndUncertainPostCannotBeRetried()
    {
        var posts = 0; var store = new MemoryStore();
        using var http = Client(r =>
        {
            if (r.Method == HttpMethod.Post) { posts++; throw new HttpRequestException("synthetic network loss"); }
            return Task.FromResult(ReadResponse(r));
        });
        var controller = Create(http, store);
        await controller.SetSessionAsync(App("a"), null, true);
        store.FailWrites = true;
        Assert.False(await controller.GenerateReportAsync());
        Assert.Equal(0, posts);
        store.FailWrites = false;
        Assert.False(await controller.GenerateReportAsync());
        Assert.Equal(1, posts);
        Assert.True(controller.GenerationUnknown);
        Assert.False(await controller.GenerateReportAsync());
        Assert.Equal(1, posts);
        Assert.True(await controller.RefreshGsmAsync());
        Assert.True(controller.GenerationUnknown); // An old successful job cannot authorize another send.
        var restored = Create(http, store);
        await restored.SetSessionAsync(App("a"), null, false);
        Assert.True(restored.GenerationUnknown);
    }

    [Fact]
    public async Task ReportLimitCountsPosAndArmRowsTogether()
    {
        var posts = 0;
        using var http = Client(r =>
        {
            if (r.Method == HttpMethod.Post) { posts++; throw new HttpRequestException("unexpected post"); }
            if (r.RequestUri!.AbsolutePath == "/api/v2/routes")
            {
                var rows = Enumerable.Range(1, 16).SelectMany(day => new[] { "POS", "ARM" }.Select(type => new JsonObject
                    { ["date"] = DateTime.Now.ToString("yyyy-MM") + "-" + day.ToString("D2"), ["workType"] = type, ["stops"] = new JsonArray(), ["sent"] = false })).ToArray();
                return Task.FromResult(Response(new JsonObject { ["records"] = JsonSerializer.SerializeToNode(rows) }.ToJsonString()));
            }
            return Task.FromResult(ReadResponse(r));
        });
        var controller = Create(http, new MemoryStore());
        await controller.SetSessionAsync(App("a"), null, true);
        Assert.Equal(32, controller.MonthData.Routes.Count);
        Assert.False(await controller.GenerateReportAsync());
        Assert.Equal(0, posts);
        Assert.Contains("31", controller.ErrorMessage);
    }

    [Fact]
    public async Task FailedCacheReadCannotEraseDurablePendingReportOrEnableAnotherPost()
    {
        var posts = 0; var store = new MemoryStore();
        using var http = Client(r =>
        {
            if (r.Method == HttpMethod.Post) { posts++; throw new HttpRequestException("synthetic uncertain result"); }
            return Task.FromResult(ReadResponse(r));
        });
        var first = Create(http, store);
        await first.SetSessionAsync(App("a"), null, true);
        Assert.False(await first.GenerateReportAsync());
        Assert.Equal(1, posts);
        store.FailReads = true;
        var restored = Create(http, store);
        await restored.SetSessionAsync(App("a"), null, true);
        Assert.False(await restored.GenerateReportAsync());
        Assert.Equal(1, posts);
        Assert.True(restored.GenerationUnknown);
        Assert.Contains(store.Documents.Values, doc => doc["pendingReport"] is JsonObject);
    }

    [Theory]
    [InlineData(false, "SENT")]
    [InlineData(false, "FAILED")]
    [InlineData(false, "SKIPPED")]
    [InlineData(true, "SENT")]
    public async Task OnlyOneNewTerminalJobCanResolvePendingReport(bool concurrentProcessingJob, string terminalStatus)
    {
        var month = DateTime.Now.ToString("yyyy-MM");
        JsonObject Job(string id, string status) => new() { ["id"] = id, ["month"] = month, ["status"] = status,
            ["createdAt"] = DateTimeOffset.UtcNow.AddMinutes(1).ToString("O") };
        JsonObject[] jobs = [Job("baseline", "SENT")];
        using var http = Client(r =>
        {
            if (r.Method == HttpMethod.Post) throw new HttpRequestException("uncertain result");
            if (r.RequestUri!.AbsolutePath == "/api/v2/gsm/jobs") return Task.FromResult(Response(new JsonObject { ["jobs"] = JsonSerializer.SerializeToNode(jobs) }.ToJsonString()));
            return Task.FromResult(ReadResponse(r));
        });
        var controller = Create(http, new MemoryStore());
        await controller.SetSessionAsync(App("a"), null, true);
        Assert.False(await controller.GenerateReportAsync());
        Assert.True(await controller.RefreshGsmAsync());
        Assert.True(controller.GenerationUnknown); // Baseline ID is never mistaken for our new report.
        jobs = concurrentProcessingJob ? [Job("baseline", "SENT"), Job("new-sent", terminalStatus), Job("new-processing", "RUNNING")]
            : [Job("baseline", "SENT"), Job("new-sent", terminalStatus)];
        Assert.True(await controller.RefreshGsmAsync());
        Assert.Equal(concurrentProcessingJob, controller.GenerationUnknown);
    }

    [Fact]
    public async Task LateDiskWriteStaysUnderCapturedAccountAndCannotPublishAfterAccountSwitch()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemoryStore { BeforeSave = async () => { entered.TrySetResult(); await release.Task; } };
        using var http = Client(r => Task.FromResult(ReadResponse(r)));
        var controller = Create(http, store);
        await controller.SetSessionAsync(App("a"), null, false);
        var importing = controller.ImportClosedAsync(Preview("A-ONLY"));
        await entered.Task;
        await controller.SetSessionAsync(App("b"), null, false);
        release.SetResult();
        Assert.False(await importing);
        Assert.Empty(controller.ClosedRequests);
        var sameAccountB = Create(http, store);
        await sameAccountB.SetSessionAsync(App("b"), null, false);
        Assert.Empty(sameAccountB.ClosedRequests);
        var previousAccountA = Create(http, store);
        await previousAccountA.SetSessionAsync(App("a"), null, false);
        Assert.Equal("A-ONLY", Assert.Single(previousAccountA.ClosedRequests).Number);
    }

    [Fact]
    public async Task MalformedPendingMarkerCannotBeSilentlyReplacedWithReadyState()
    {
        var posts = 0; var store = new MemoryStore();
        using var http = Client(r =>
        {
            if (r.Method == HttpMethod.Post) { posts++; throw new HttpRequestException("unexpected post"); }
            return Task.FromResult(ReadResponse(r));
        });
        await Create(http, store).SetSessionAsync(App("a"), null, true);
        Assert.Single(store.Documents).Value["pendingReport"] = "damaged";
        var restored = Create(http, store);
        await restored.SetSessionAsync(App("a"), null, true);
        Assert.False(await restored.GenerateReportAsync());
        Assert.Equal(0, posts);
        Assert.True(restored.GenerationUnknown);
    }

    [Fact]
    public async Task ExpiredSimpleOneSessionOnlyFlagsSimpleOneAndRetainsGsmAndArchive()
    {
        var expired = false;
        using var http = Client(r =>
        {
            if (r.RequestUri!.Host == "so.example.invalid") return Task.FromResult(expired
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Response("""{"data":{"items":[{"sys_id":"closed-1","number":"REQ1","state":"closed","multicard_terminal_address":"Адрес"}],"total":1}}"""));
            return Task.FromResult(ReadResponse(r));
        });
        var controller = Create(http, new MemoryStore());
        await controller.SetSessionAsync(App("a"), So("so1"), true);
        var profile = controller.ProfileResult;
        var updated = controller.LastGsmUpdated;
        Assert.Equal("REQ1", Assert.Single(controller.ClosedRequests).Number);
        expired = true;
        Assert.False(await controller.RefreshClosedAsync());
        Assert.True(controller.RequiresSimpleOneReauthentication);
        Assert.False(controller.RequiresReauthentication);
        Assert.Same(profile, controller.ProfileResult);
        Assert.Equal(updated, controller.LastGsmUpdated);
        Assert.Equal("REQ1", Assert.Single(controller.ClosedRequests).Number);
        expired = false;
        Assert.True(await controller.RefreshClosedAsync());
        Assert.False(controller.RequiresSimpleOneReauthentication);
        expired = true;
        Assert.False(await controller.RefreshClosedAsync());
        Assert.True(controller.RequiresSimpleOneReauthentication);
        await controller.SetSessionAsync(App("a"), null, true);
        Assert.False(controller.RequiresSimpleOneReauthentication);
        Assert.Same(profile, controller.ProfileResult);
    }

    [Fact]
    public async Task ExpiredLumaSessionDoesNotSetSimpleOneReauthenticationFlag()
    {
        var expired = false;
        using var http = Client(r => Task.FromResult(expired ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : ReadResponse(r)));
        var controller = Create(http, new MemoryStore());
        await controller.SetSessionAsync(App("a"), null, true);
        var profile = controller.ProfileResult;
        expired = true;
        Assert.False(await controller.RefreshGsmAsync());
        Assert.True(controller.RequiresReauthentication);
        Assert.False(controller.RequiresSimpleOneReauthentication);
        Assert.Same(profile, controller.ProfileResult);
    }

    [Fact]
    public async Task ArmRoutesNeedArmProjectBeforeReportMarkerOrPost()
    {
        var posts = 0; var store = new MemoryStore();
        using var http = Client(r =>
        {
            if (r.Method == HttpMethod.Post) { posts++; throw new HttpRequestException("unexpected post"); }
            if (r.RequestUri!.AbsolutePath == "/api/v2/routes") return Task.FromResult(Response(new JsonObject { ["records"] = new JsonArray(new JsonObject
                { ["date"] = DateTime.Now.ToString("yyyy-MM") + "-01", ["workType"] = "ARM", ["sent"] = false, ["stops"] = new JsonArray() }) }.ToJsonString()));
            return Task.FromResult(ReadResponse(r));
        });
        var controller = Create(http, store);
        await controller.SetSessionAsync(App("a"), null, true);
        Assert.False(await controller.GenerateReportAsync());
        Assert.Equal(0, posts);
        Assert.False(controller.GenerationUnknown);
        Assert.Contains("ARM", controller.ErrorMessage);
        Assert.All(store.Documents.Values, document => Assert.Null(document["pendingReport"]));
    }

    [Theory]
    [InlineData(400, "{\"error\":\"GSM_PROFILE_REQUIRED\",\"message\":\"Заполните проект ARM.\"}", "Сервер отклонил запрос до формирования отчёта. Проверьте обязательные поля и проекты ГСМ-профиля.", false)]
    [InlineData(401, "{}", "Сессия LumaWork истекла. Войдите снова.", true)]
    public async Task ConfirmedReportRejectionClearsDurableMarkerAndPreservesPreciseError(int status, string body, string expectedError, bool invalidates)
    {
        var posts = 0; var store = new MemoryStore();
        using var http = Client(r =>
        {
            if (r.Method == HttpMethod.Post) { posts++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }); }
            return Task.FromResult(ReadResponse(r));
        });
        var controller = Create(http, store);
        await controller.SetSessionAsync(App("a"), null, true);
        Assert.False(await controller.GenerateReportAsync());
        Assert.Equal(1, posts);
        Assert.False(controller.GenerationUnknown);
        Assert.Equal(expectedError, controller.ErrorMessage);
        Assert.Equal(invalidates, controller.RequiresReauthentication);
        Assert.All(store.Documents.Values, document => Assert.Null(document["pendingReport"]));
        var restored = Create(http, store);
        await restored.SetSessionAsync(App("a"), null, false);
        Assert.False(restored.GenerationUnknown);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    public async Task AmbiguousReportServerFailureKeepsDurableUnknownMarker(int status)
    {
        var posts = 0; var store = new MemoryStore();
        using var http = Client(r =>
        {
            if (r.Method == HttpMethod.Post) { posts++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") }); }
            return Task.FromResult(ReadResponse(r));
        });
        var controller = Create(http, store);
        await controller.SetSessionAsync(App("a"), null, true);
        Assert.False(await controller.GenerateReportAsync());
        Assert.True(controller.GenerationUnknown);
        Assert.Contains(store.Documents.Values, document => document["pendingReport"] is JsonObject);
        Assert.False(await controller.GenerateReportAsync());
        Assert.Equal(1, posts);
    }

    [Fact]
    public async Task RejectionWithFailedMarkerClearKeepsDurableLockAndAuthInvalidation()
    {
        var posts = 0; var store = new MemoryStore();
        using var http = Client(r =>
        {
            if (r.Method == HttpMethod.Post)
            {
                posts++; store.FailWrites = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
            return Task.FromResult(ReadResponse(r));
        });
        var controller = Create(http, store);
        await controller.SetSessionAsync(App("a"), null, true);
        Assert.False(await controller.GenerateReportAsync());
        Assert.True(controller.RequiresReauthentication);
        Assert.True(controller.GenerationUnknown);
        Assert.Contains(store.Documents.Values, document => document["pendingReport"] is JsonObject);
        Assert.False(await controller.GenerateReportAsync());
        Assert.Equal(1, posts);
    }

    private static WorkspaceController Create(HttpClient http, MemoryStore store) => new(new GsmApi(new Uri("https://luma.example.invalid"), http),
        new SimpleOneClosedRequestsApi(new Uri("https://so.example.invalid/v1"), http, new("11", "22", "33")), store, "synthetic-origins");
    private static AppSession App(string id) => new("secret-token-" + id, new(id, id + "@example.invalid", "engineer"));
    private static SimpleOneSession So(string id) => new("PRIVATE_COOKIE-" + id, new(id, id, "", ""));
    private static ClosedRequestsImportPreview Preview(string number) => new([new(number, number, "Адрес", "closed", "", "", "", "")], 0, 0);
    private static HttpResponseMessage ReadResponse(HttpRequestMessage request)
    {
        var month = DateTime.Now.ToString("yyyy-MM");
        return request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/gsm/profile" => Response(new JsonObject { ["profile"] = ValidProfile(), ["availableFuelTypes"] = new JsonArray("АИ-95") }.ToJsonString()),
            "/api/v2/gsm/projects" => Response("""{"projects":[{"id":"pos","name":"POS","budgetCode":"B"}]}"""),
            "/api/v2/routes" => Response("""{"records":[]}"""),
            "/api/v2/fuel" => Response("""{"records":[]}"""),
            "/api/v2/gsm/jobs" => Response("""{"jobs":[{"id":"old","month":"2000-01","status":"SENT","createdAt":"2000-01-01T00:00:00Z"}]}"""),
            _ => Response(new JsonObject { ["month"] = month, ["startOdometer"] = 1000 }.ToJsonString())
        };
    }
    private static JsonObject ValidProfile()
    {
        var profile = new JsonObject();
        foreach (var key in new[] { "employeeFullName", "employeeShortName", "employeeReportSignName", "authorizedFullName", "authorizedShortName", "employeeJobTitle", "employeeCompany", "employeeAddress", "driverLicenseNumber", "fuelCardNumber", "carModel", "licensePlate" }) profile[key] = "Тест";
        profile["employeePhone"] = "9991112233"; profile["posProjectId"] = "pos"; profile["fuelNorm"] = 9;
        profile["fuelTypes"] = new JsonArray("АИ-95"); profile["reportStartMonth"] = "2020-01";
        profile["future"] = new JsonObject { ["value"] = "keep" }; profile["password"] = "PRIVATE_PASSWORD";
        return profile;
    }
    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => new(new Handler(handler));
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
    private sealed class MemoryStore : IWorkspaceStore
    {
        public Dictionary<string, JsonObject> Documents { get; } = [];
        public bool FailWrites { get; set; }
        public bool FailReads { get; set; }
        public Func<Task>? BeforeSave { get; set; }
        public Task<JsonObject?> LoadAsync(string key, CancellationToken cancellationToken)
        {
            if (FailReads) throw new IOException("synthetic read failure");
            return Task.FromResult(Documents.TryGetValue(key, out var value) ? (JsonObject)value.DeepClone() : null);
        }
        public async Task SaveAsync(string key, JsonObject document, CancellationToken cancellationToken)
        {
            if (FailWrites) throw new IOException("synthetic full disk");
            if (BeforeSave != null) await BeforeSave(); // Deliberately ignores cancellation to exercise the publication guard.
            Documents[key] = (JsonObject)document.DeepClone();
        }
    }
}
