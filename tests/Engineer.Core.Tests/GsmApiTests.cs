using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Engineer.Core.Auth;
using Engineer.Core.Gsm;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class GsmApiTests
{
    private static readonly Uri Origin = new("https://luma.example.invalid");

    [Fact]
    public async Task ProfileLoadsAbsentAndLegacyDocumentsWithoutLosingIds()
    {
        using var http = Client(_ => Response("""{"profile":{"vehicle_id":"vehicle-fixture","pos_project_id":"pos-fixture","armProjectId":"arm-fixture","fuel_norm":"9,5","fuelType":"АИ-95","custom":{"value":3}},"availableFuelTypes":["АИ-95"]}"""));
        var result = await new GsmApi(Origin, http).FetchProfileAsync("synthetic");
        Assert.Equal("vehicle-fixture", GsmValues.Text(result.Profile!, "vehicleId"));
        Assert.Equal("pos-fixture", GsmValues.Text(result.Profile!, "posProjectId"));
        Assert.Equal("arm-fixture", GsmValues.Text(result.Profile!, "armProjectId"));
        Assert.Equal(9.5, GsmValues.Number(result.Profile!, "fuelNorm"));
        Assert.Equal(3, result.Profile!["custom"]!["value"]!.GetValue<int>());
        Assert.Equal("АИ-95", Assert.Single(result.AvailableFuelTypes));
        using var empty = Client(_ => Response("""{"profile":null,"availableFuelTypes":[]}"""));
        Assert.Null((await new GsmApi(Origin, empty).FetchProfileAsync("synthetic")).Profile);
    }

    [Fact]
    public async Task ProjectsComeFromServerDirectory()
    {
        using var http = Client(request =>
        {
            Assert.Equal("/api/v2/gsm/projects", request.RequestUri!.AbsolutePath);
            return Response("""{"projects":[{"id":"p1","name":"Тестовый проект","budgetCode":"B1"}]}""");
        });
        var project = Assert.Single(await new GsmApi(Origin, http).FetchProjectsAsync("synthetic"));
        Assert.Equal(new GsmProject("p1", "Тестовый проект", "B1"), project);
        Assert.Equal(project.Name, project.ToString());
    }

    [Fact]
    public async Task MonthFetchUsesLeapYearBoundsAndOnlyActualRefuels()
    {
        var calls = new List<string>();
        using var http = Client(request =>
        {
            calls.Add(request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer synthetic", request.Headers.Authorization!.ToString());
            Assert.False(request.Headers.Contains("Cookie"));
            return request.RequestUri.AbsolutePath switch
            {
                "/api/v2/routes" => Response("""{"records":[{"date":"2028-02-29","workType":"ARM","sent":true,"distanceKm":"12","stops":[{"id":"s1","payload":{"coordinateOverride":{"latitude":45,"longitude":35}}}]},{"date":"2028-03-01"}]}"""),
                "/api/v2/fuel" => Response("""{"records":[{"id":"actual","recordType":"FUEL","date":"2028-02-29","liters":"4,5","fuelCost":"300"},{"id":"adjust","recordType":"ADJUSTMENT","date":"2028-02-01","liters":5},{"id":"mileage","recordType":"FUEL","date":"2028-02-01","mileage":200},{"id":"outside","recordType":"FUEL","date":"2028-01-31","liters":8}]}"""),
                _ => Response("""{"month":"2028-02","startOdometer":"12000"}""")
            };
        });
        var result = await new GsmApi(Origin, http).FetchMonthAsync("synthetic", "2028-02");
        Assert.Contains("/api/v2/routes?from=2028-02-01&to=2028-02-29", calls);
        Assert.Contains("/api/v2/gsm/odometer-suggestion?month=2028-02", calls);
        Assert.Equal(12000, result.StartOdometer);
        Assert.Equal("actual", GsmValues.Text(Assert.Single(result.Fuel), "id"));
        Assert.Equal(4.5, GsmValues.Number(result.Fuel[0], "liters"));
        var route = Assert.Single(result.Routes);
        Assert.Equal(12, GsmValues.Number(route, "distanceKm"));
        Assert.Equal(45, route["stops"]![0]!["coordinateOverride"]!["latitude"]!.GetValue<int>());
    }

    [Fact]
    public async Task MonthFetchSupportsLegacyDaysAndPreservesBothWorkTypes()
    {
        using var http = Client(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/routes" => Response("""{"days":{"2026-10-01":{"sent":false,"stops":[]},"2026-10-01|ARM":{"sent":false,"stops":[]}}}"""),
            "/api/v2/fuel" => Response("""{"records":[]}"""),
            _ => Response("""{"month":"2026-10","startOdometer":null}""")
        });
        var result = await new GsmApi(Origin, http).FetchMonthAsync("synthetic", "2026-10");
        Assert.Equal(new[] { "POS", "ARM" }, result.Routes.Select(row => GsmValues.Text(row, "workType")));
        Assert.Null(result.StartOdometer);
    }

    [Fact]
    public async Task RoutePutClonesAndPreservesStopsSummarySentAndUnknownFields()
    {
        var route = JsonNode.Parse("""{"date":"2026-10-01","workType":"ARM","sent":true,"distanceKm":25,"periodStartOdometer":1000,"reportedDistanceKm":40,"routeSummary":"A-B","requestNumbersSummary":"R1","future":{"flag":true},"stops":[{"id":"stop-fixture","address":"A","status":"Выполнена","rejectReason":"","coordinateOverride":{"latitude":45,"longitude":35},"hidden":"keep"}]}""")!.AsObject();
        var snapshot = route.ToJsonString();
        using var http = Client(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/api/v2/routes/2026-10-01", request.RequestUri!.AbsolutePath);
            var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            Assert.True(JsonNode.DeepEquals(route, payload));
            return await Response(payload.ToJsonString());
        });
        var saved = await new GsmApi(Origin, http).SaveRouteAsync("synthetic", route);
        Assert.True(JsonNode.DeepEquals(route, saved));
        Assert.Equal(snapshot, route.ToJsonString());
    }

    [Fact]
    public async Task PartialRouteIsRejectedBeforeItCouldEraseStopsOrSentState()
    {
        using var http = Client(_ => throw new Exception("Must not send"));
        await Assert.ThrowsAsync<ArgumentException>(() => new GsmApi(Origin, http).SaveRouteAsync("synthetic",
            new JsonObject { ["date"] = "2026-10-01", ["distanceKm"] = 10 }));
    }

    [Fact]
    public async Task ProfilePutRetainsVehicleAndBothProjectIdsAndNormalizesNumericStrings()
    {
        var profile = ValidProfile();
        profile["fuelNorm"] = "9,5";
        var original = profile.ToJsonString();
        using var http = Client(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            foreach (var key in new[] { "vehicleId", "posProjectId", "armProjectId", "budgetCode" })
                Assert.Equal(GsmValues.Text(profile, key), GsmValues.Text(payload, key));
            Assert.Equal(9.5, payload["fuelNorm"]!.GetValue<double>());
            Assert.Equal("+7(999)000-00-00", GsmValues.Text(payload, "employeePhone"));
            return await Response(new JsonObject { ["profile"] = payload }.ToJsonString());
        });
        await new GsmApi(Origin, http).SaveProfileAsync("synthetic", profile);
        Assert.Equal(original, profile.ToJsonString());
    }

    [Theory]
    [InlineData("{\"success\":true,\"month\":\"2026-10\",\"generated\":true}", false)]
    [InlineData("{\"success\":true,\"month\":\"2026-10\",\"sent\":true}", true)]
    [InlineData("{\"success\":true,\"month\":\"2026-10\",\"status\":\"SENT\"}", true)]
    public async Task ReportDeliveryRequiresSentEvidence(string response, bool delivered)
    {
        var calls = 0;
        using var http = Client(async request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v2/gsm/report", request.RequestUri!.AbsolutePath);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            Assert.Single(body);
            Assert.Equal("2026-10", body["month"]!.GetValue<string>());
            return await Response(response);
        });
        var result = await new GsmApi(Origin, http).SendReportAsync("synthetic", "2026-10");
        Assert.Equal(delivered, result.ConfirmsEmailDelivery);
        Assert.Equal(1, calls);
        if (!delivered) Assert.Contains("не подтверждена", result.Message);
    }

    [Fact]
    public async Task ReportTransportFailureIsUncertainAndNeverRetried()
    {
        var calls = 0;
        using var http = Client(_ => { calls++; throw new HttpRequestException("Synthetic interruption"); });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new GsmApi(Origin, http).SendReportAsync("synthetic", "2026-10"));
        Assert.Contains("неизвестен", error.Message);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(400, "GSM_PROFILE_REQUIRED", false)]
    [InlineData(400, "VALIDATION_ERROR", false)]
    [InlineData(401, "UNAUTHORIZED", true)]
    [InlineData(403, "USER_BLOCKED", true)]
    public async Task DocumentedPreGenerationRejectionIsTypedAndSafeToClearPending(int status, string errorCode, bool invalidates)
    {
        var calls = 0;
        using var http = Client(_ =>
        {
            calls++;
            return ErrorResponse(status, errorCode);
        });
        var error = await Assert.ThrowsAsync<GsmReportRejectedException>(() => new GsmApi(Origin, http).SendReportAsync("synthetic", "2026-10"));
        Assert.Equal(invalidates, error.InvalidatesSession);
        Assert.Equal(1, calls);
        Assert.DoesNotContain("untrusted server detail", error.Message);
    }

    [Theory]
    [InlineData(400, "UNKNOWN")]
    [InlineData(403, "UNKNOWN")]
    [InlineData(500, "GSM_PROFILE_REQUIRED")]
    [InlineData(500, "VALIDATION_ERROR")]
    [InlineData(500, "USER_BLOCKED")]
    [InlineData(500, "GSM_REPORT_NOT_SENT")]
    public async Task UnrecognizedOrPostGenerationRejectionKeepsOutcomeAmbiguous(int status, string errorCode)
    {
        using var http = Client(_ => ErrorResponse(status, errorCode));
        var error = await Assert.ThrowsAsync<AuthException>(() => new GsmApi(Origin, http).SendReportAsync("synthetic", "2026-10"));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(errorCode, error.ErrorCode);
        Assert.DoesNotContain("untrusted server detail", error.Message);
    }

    [Fact]
    public async Task TransportIncludesCurrentBuildAndPreservesTypedHttpErrorMetadata()
    {
        using var http = Client(request =>
        {
            Assert.Equal("5", Assert.Single(request.Headers.GetValues("X-LumaWork-Build")));
            return ErrorResponse(403, "USER_BLOCKED");
        });
        var error = await Assert.ThrowsAsync<AuthException>(() => new GsmApi(Origin, http).FetchProfileAsync("synthetic"));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal("USER_BLOCKED", error.ErrorCode);
        Assert.True(error.InvalidatesSession);
    }

    [Fact]
    public async Task UnauthorizedResponseInvalidatesSession()
    {
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var error = await Assert.ThrowsAsync<AuthException>(() => new GsmApi(Origin, http).FetchProfileAsync("synthetic"));
        Assert.True(error.InvalidatesSession);
    }

    [Theory]
    [InlineData("2026-2")]
    [InlineData("2026-13")]
    [InlineData("2026-10&force=true")]
    [InlineData("0000-01")]
    public async Task InvalidMonthsNeverReachServer(string month)
    {
        using var http = Client(_ => throw new Exception("Must not send"));
        var api = new GsmApi(Origin, http);
        await Assert.ThrowsAsync<ArgumentException>(() => api.FetchMonthAsync("synthetic", month));
        await Assert.ThrowsAsync<ArgumentException>(() => api.SendReportAsync("synthetic", month));
    }

    [Fact]
    public async Task RefuelCreateAndEditUseCorrectMethodsAndRejectFinancialRecords()
    {
        var calls = new List<string>();
        using var http = Client(async request =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            return await Response(await request.Content!.ReadAsStringAsync());
        });
        var api = new GsmApi(Origin, http);
        var fuel = new JsonObject { ["date"] = "2026-10-01", ["liters"] = "10,5", ["fuelCost"] = 700, ["fuelType"] = "АИ-95", ["comment"] = "keep" };
        await api.SaveRefuelAsync("synthetic", fuel);
        fuel["id"] = "fuel-fixture";
        fuel["recordType"] = "FUEL";
        await api.SaveRefuelAsync("synthetic", fuel);
        fuel["recordType"] = "adjustment";
        await Assert.ThrowsAsync<ArgumentException>(() => api.SaveRefuelAsync("synthetic", fuel));
        Assert.Equal(new[] { "POST /api/v2/fuel", "PUT /api/v2/fuel/fuel-fixture" }, calls);
    }

    [Fact]
    public async Task JobsAreFetchedReadOnly()
    {
        using var http = Client(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v2/gsm/jobs", request.RequestUri!.AbsolutePath);
            return Response("""{"jobs":[{"id":"job-fixture","month":"2026-10","status":"GENERATED"}]}""");
        });
        Assert.Equal("GENERATED", GsmValues.Text(Assert.Single(await new GsmApi(Origin, http).FetchJobsAsync("synthetic")), "status"));
    }

    [Fact]
    public void ProfileValidationChecksContactsProjectNumbersFuelDirectoryAndMonth()
    {
        var profile = ValidProfile();
        Assert.Empty(GsmProfileValidation.Validate(profile, ["АИ-95"]));
        profile["employeePhone"] = "+7(999)";
        profile["posProjectId"] = "";
        profile["fuelNorm"] = -1;
        profile["reportStartMonth"] = "2026-15";
        Assert.Equal(5, GsmProfileValidation.Validate(profile, ["ДТ"]).Count);
    }

    private static JsonObject ValidProfile() => JsonNode.Parse("""{"vehicleId":"vehicle-fixture","posProjectId":"pos-fixture","armProjectId":"arm-fixture","budgetCode":"B1","employeeFullName":"Тестовый сотрудник","employeeShortName":"Тестовый С.","employeeReportSignName":"Тестовый С.","authorizedFullName":"Уполномоченный Тест","authorizedShortName":"Уполномоченный Т.","employeeJobTitle":"Инженер","employeeCompany":"Тестовая компания","employeeAddress":"Тестовый адрес","employeePhone":"89990000000","driverLicenseNumber":"fixture-license","fuelCardNumber":"fixture-card","carModel":"Тестовый автомобиль","licensePlate":"fixture-plate","fuelNorm":9,"fuelTypes":["АИ-95"],"reportStartMonth":"2026-04","defaultStartOdometer":0}""")!.AsObject();
    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => new(new Handler(handler));
    private static Task<HttpResponseMessage> Response(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    private static Task<HttpResponseMessage> ErrorResponse(int status, string errorCode) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
    {
        Content = new StringContent(new JsonObject { ["error"] = errorCode, ["message"] = "untrusted server detail" }.ToJsonString(), Encoding.UTF8, "application/json")
    });
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
