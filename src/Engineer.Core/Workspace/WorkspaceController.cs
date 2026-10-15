using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engineer.Core.Auth;
using Engineer.Core.Gsm;
using Engineer.Core.ClosedRequests;

namespace Engineer.Core.Workspace;

public interface IWorkspaceStore
{
    Task<JsonObject?> LoadAsync(string key, CancellationToken cancellationToken);
    Task SaveAsync(string key, JsonObject document, CancellationToken cancellationToken);
}

// Owned by the UI context, like SessionController. Every publication is guarded by its captured context.
public sealed class WorkspaceController(GsmApi gsm, SimpleOneClosedRequestsApi closed, IWorkspaceStore store, string originScope)
{
    private AppSession? app;
    private SimpleOneSession? so;
    private bool online;
    private long revision;
    private CancellationTokenSource? operation;
    private JsonObject? pendingReport;
    private bool gsmCacheLoaded;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public string Month { get; private set; } = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    public GsmProfileResult ProfileResult { get; private set; } = new(null, []);
    public IReadOnlyList<GsmProject> Projects { get; private set; } = [];
    public GsmMonthData MonthData { get; private set; } = new(DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture), [], [], null);
    public IReadOnlyList<JsonObject> Jobs { get; private set; } = [];
    public IReadOnlyList<ClosedRequest> ClosedRequests { get; private set; } = [];
    public bool IsBusy => operation != null;
    public string? ErrorMessage { get; private set; }
    public string? InfoMessage { get; private set; }
    public DateTimeOffset? LastGsmUpdated { get; private set; }
    public DateTimeOffset? LastClosedUpdated { get; private set; }
    public bool GenerationUnknown => pendingReport != null || app != null && !gsmCacheLoaded;
    public bool RequiresReauthentication { get; private set; }
    public bool RequiresSimpleOneReauthentication { get; private set; }
    public event Action? Changed;

    public async Task SetSessionAsync(AppSession? app, SimpleOneSession? so, bool online)
    {
        so = app == null ? null : so;
        online = app != null && online;
        if (this.app?.User.Id == app?.User.Id && this.app?.Token == app?.Token
            && this.so?.User.Id == so?.User.Id && this.so?.AuthKey == so?.AuthKey && this.online == online) return;
        var appChanged = this.app?.User.Id != app?.User.Id || this.app?.Token != app?.Token;
        var soChanged = this.so?.User.Id != so?.User.Id || this.so?.AuthKey != so?.AuthKey;
        RequiresReauthentication = false; RequiresSimpleOneReauthentication = false;
        Cancel();
        this.app = app; this.so = so; this.online = online;
        ErrorMessage = null; InfoMessage = null; RequiresReauthentication = false; RequiresSimpleOneReauthentication = false;
        if (appChanged) ClearGsm();
        if (appChanged || soChanged) { ClosedRequests = []; LastClosedUpdated = null; }
        Changed?.Invoke();
        if (app == null) return;
        await RunAsync(async (context, ct) =>
        {
            if (appChanged) await TryPartAsync(() => LoadGsmAsync(context, ct), context, ct);
            if (appChanged || soChanged) await TryPartAsync(() => LoadClosedAsync(context, ct), context, ct);
            Check(context, ct); Changed?.Invoke(); // Cached values are visible while the network refresh runs.
            if (context.Online)
            {
                if (appChanged || !soChanged) await TryPartAsync(() => FetchGsmAsync(context, ct), context, ct);
                if (context.SimpleOne != null) await TryPartAsync(() => FetchClosedAsync(context, ct), context, ct);
            }
        });
    }

    public async Task<bool> SelectMonthAsync(string month)
    {
        if (!GsmValues.IsValidMonth(month)) return Fail("Месяц должен быть в формате YYYY-MM.");
        if (Month == month) return true;
        Cancel(); Month = month; ClearGsm(); Changed?.Invoke();
        return await RunAsync(async (context, ct) =>
        {
            await TryPartAsync(() => LoadGsmAsync(context, ct), context, ct);
            Check(context, ct); Changed?.Invoke();
            if (context.Online) await FetchGsmAsync(context, ct);
        });
    }

    public Task<bool> RefreshGsmAsync() => RunAsync(FetchGsmAsync, requireOnline: true);
    public Task<bool> RefreshClosedAsync()
    {
        if (so == null) return Task.FromResult(Fail("Подключите SimpleOne или импортируйте XLSX вручную."));
        return RunAsync(FetchClosedAsync, requireOnline: true);
    }
    public Task<bool> ImportClosedAsync(ClosedRequestsImportPreview preview) => RunAsync(async (context, ct) =>
    {
        var records = ClosedRequestsMerge.Upsert(ClosedRequests, preview.Records);
        var updated = DateTimeOffset.UtcNow;
        await SaveAsync(context.ClosedKey, new JsonObject { ["version"] = 1, ["records"] = Node(records), ["updatedAt"] = updated.ToString("O") }, context, ct);
        ClosedRequests = records; LastClosedUpdated = updated;
        InfoMessage = $"Импортировано: {preview.Records.Count}. Отклонено: {preview.RejectedCount}. Без адреса: {preview.MissingAddressCount}.";
    });
    public Task<bool> SaveProfileAsync(JsonObject profile) => MutateAsync((context, ct) => gsm.SaveProfileAsync(context.App.Token, profile, ct));
    public Task<bool> SaveRouteAsync(JsonObject route) => MutateAsync((context, ct) => gsm.SaveRouteAsync(context.App.Token, route, ct));
    public Task<bool> SaveRefuelAsync(JsonObject fuel) => MutateAsync((context, ct) => gsm.SaveRefuelAsync(context.App.Token, fuel, ct));

    private Task<bool> MutateAsync(Func<Context, CancellationToken, Task> mutation) => RunAsync(async (context, ct) =>
    {
        try
        {
            await mutation(context, ct); Check(context, ct);
            await FetchGsmAsync(context, ct);
            InfoMessage = "Изменения сохранены и проверены загрузкой с сервера.";
        }
        catch (ArgumentException) { throw; }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Check(context, ct);
            if (error is AuthException { InvalidatesSession: true }) RequiresReauthentication = true;
            throw new InvalidOperationException("Результат сохранения не подтверждён. Обновите данные перед повторным сохранением.", error);
        }
    }, requireOnline: true);

    public Task<bool> GenerateReportAsync() => RunAsync(async (context, ct) =>
    {
        if (GenerationUnknown) throw new InvalidOperationException("Результат предыдущей отправки неизвестен. Обновите историю отчётов; повторная отправка заблокирована.");
        if (ProfileResult.Profile is not { } profile) throw new ArgumentException("Заполните профиль ГСМ перед отправкой отчёта.");
        var errors = GsmProfileValidation.Validate(profile, ProfileResult.AvailableFuelTypes);
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
        if (MonthData.Month != context.Month || !GsmValues.IsValidMonth(context.Month)) throw new ArgumentException("Обновите данные выбранного месяца.");
        if (string.CompareOrdinal(context.Month, GsmValues.Text(profile, "reportStartMonth")) < 0) throw new ArgumentException("Месяц отчёта раньше начального месяца профиля.");
        if (MonthData.Routes.Count > 31)
            throw new ArgumentException("В отчёте допускается не более 31 строки маршрутов суммарно для POS и ARM.");
        if (MonthData.Routes.Any(route => GsmValues.Text(route, "workType") == "ARM") && GsmValues.Text(profile, "armProjectId").Length == 0)
            throw new ArgumentException("Для маршрутов ARM выберите проект ARM в профиле ГСМ.");
        var marker = new JsonObject { ["month"] = context.Month, ["startedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["baselineJobIds"] = Node(Jobs.Select(j => GsmValues.Text(j, "id")).Where(id => id.Length > 0).ToArray()) };
        await SaveGsmAsync(context, ProfileResult, Projects, MonthData, Jobs, LastGsmUpdated, marker, ct);
        Check(context, ct); pendingReport = marker; Changed?.Invoke();
        try
        {
            var result = await gsm.SendReportAsync(context.App.Token, context.Month, ct);
            Check(context, ct);
            // Only an explicit sent/status=SENT result confirms email delivery.
            InfoMessage = result.Message;
            if (result.ConfirmsEmailDelivery)
            {
                await SaveGsmAsync(context, ProfileResult, Projects, MonthData, Jobs, LastGsmUpdated, null, ct);
                Check(context, ct); pendingReport = null;
            }
            else await FetchGsmAsync(context, ct);
        }
        catch (GsmReportRejectedException error)
        {
            Check(context, ct);
            RequiresReauthentication |= error.InvalidatesSession;
            // The typed API rejection proves generation never created a job. Clear the marker durably first.
            await SaveGsmAsync(context, ProfileResult, Projects, MonthData, Jobs, LastGsmUpdated, null, ct);
            Check(context, ct); pendingReport = null;
            throw new InvalidOperationException(error.Message, error);
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Check(context, ct);
            if (error is AuthException { InvalidatesSession: true }) RequiresReauthentication = true;
            throw new InvalidOperationException("Результат отправки неизвестен. Обновите историю отчётов; повторная отправка заблокирована.", error);
        }
    }, requireOnline: true);

    public void Cancel()
    {
        revision++; operation?.Cancel(); operation = null;
        Changed?.Invoke();
    }

    private async Task FetchGsmAsync(Context context, CancellationToken ct)
    {
        Check(context, ct);
        // Do not overwrite an unread pending report marker with a fresh network snapshot.
        if (!gsmCacheLoaded) await LoadGsmAsync(context, ct);
        Check(context, ct);
        var profile = await gsm.FetchProfileAsync(context.App.Token, ct); Check(context, ct);
        var projects = await gsm.FetchProjectsAsync(context.App.Token, ct); Check(context, ct);
        var month = await gsm.FetchMonthAsync(context.App.Token, context.Month, ct); Check(context, ct);
        var jobs = await gsm.FetchJobsAsync(context.App.Token, ct); Check(context, ct);
        var updated = DateTimeOffset.UtcNow;
        var pending = ResolvePending(pendingReport, jobs);
        await SaveGsmAsync(context, profile, projects, month, jobs, updated, pending, ct);
        Check(context, ct);
        ProfileResult = profile; Projects = projects; MonthData = month; Jobs = jobs;
        LastGsmUpdated = updated; pendingReport = pending;
    }
    private async Task FetchClosedAsync(Context context, CancellationToken ct)
    {
        Check(context, ct);
        RequiresSimpleOneReauthentication = false;
        if (context.SimpleOne == null) throw new InvalidOperationException("Подключите SimpleOne.");
        IReadOnlyList<ClosedRequest> fetched;
        try { fetched = await closed.FetchAsync(context.SimpleOne.User.Id, context.SimpleOne.AuthKey, ct); }
        catch (AuthException error) when (error.InvalidatesSession)
        {
            Check(context, ct);
            RequiresSimpleOneReauthentication = true;
            // This credential belongs to SimpleOne; the LumaWork session remains valid.
            throw new AuthException(error.Message);
        }
        Check(context, ct);
        var records = ClosedRequestsMerge.Upsert(ClosedRequests, fetched);
        var updated = DateTimeOffset.UtcNow;
        await SaveAsync(context.ClosedKey, new JsonObject { ["version"] = 1, ["records"] = Node(records), ["updatedAt"] = updated.ToString("O") }, context, ct);
        Check(context, ct); ClosedRequests = records; LastClosedUpdated = updated;
    }
    private async Task LoadGsmAsync(Context context, CancellationToken ct)
    {
        var document = await store.LoadAsync(context.GsmKey, ct); Check(context, ct);
        if (document == null) { gsmCacheLoaded = true; return; }
        var profile = Read<GsmProfileResult>(document, "profileResult");
        var projects = Read<GsmProject[]>(document, "projects");
        var month = Read<GsmMonthData>(document, "monthData");
        var jobs = Read<JsonObject[]>(document, "jobs");
        if (month.Month != context.Month || month.Routes == null || month.Fuel == null || profile.AvailableFuelTypes == null)
            throw new InvalidDataException("Сохранённые данные ГСМ повреждены. Обновите данные.");
        var marker = document["pendingReport"];
        if (marker != null && (marker is not JsonObject markerObject || GsmValues.Text(markerObject, "month") != context.Month
            || Timestamp(markerObject, "startedAt") == null || markerObject["baselineJobIds"] is not JsonArray ids
            || ids.Any(id => id is not JsonValue value || !value.TryGetValue<string>(out _))))
            throw new InvalidDataException("Не удалось проверить сохранённое состояние отправки отчёта. Повторная отправка заблокирована.");
        ProfileResult = profile; Projects = projects; MonthData = month; Jobs = jobs;
        LastGsmUpdated = Timestamp(document, "updatedAt"); pendingReport = marker?.DeepClone() as JsonObject;
        gsmCacheLoaded = true;
    }
    private async Task LoadClosedAsync(Context context, CancellationToken ct)
    {
        var document = await store.LoadAsync(context.ClosedKey, ct); Check(context, ct);
        if (document == null) return;
        var records = Read<ClosedRequest[]>(document, "records");
        ClosedRequests = ClosedRequestsMerge.Upsert([], records); LastClosedUpdated = Timestamp(document, "updatedAt");
    }
    private Task SaveGsmAsync(Context context, GsmProfileResult profile, IReadOnlyList<GsmProject> projects, GsmMonthData month,
        IReadOnlyList<JsonObject> jobs, DateTimeOffset? updated, JsonObject? pending, CancellationToken ct)
        => SaveAsync(context.GsmKey, new JsonObject { ["version"] = 1, ["profileResult"] = Node(profile), ["projects"] = Node(projects),
            ["monthData"] = Node(month), ["jobs"] = Node(jobs), ["updatedAt"] = updated?.ToString("O"), ["pendingReport"] = pending?.DeepClone() }, context, ct);
    private async Task SaveAsync(string key, JsonObject document, Context context, CancellationToken ct)
    {
        Check(context, ct); ScrubCredentials(document);
        await store.SaveAsync(key, document, ct); Check(context, ct);
    }
    private static JsonObject? ResolvePending(JsonObject? pending, IReadOnlyList<JsonObject> jobs)
    {
        if (pending == null) return null;
        var started = Timestamp(pending, "startedAt");
        var baseline = pending["baselineJobIds"]?.Deserialize<string[]>(JsonOptions) ?? [];
        var candidates = jobs.Where(job => GsmValues.Text(job, "month") == GsmValues.Text(pending, "month")
            && GsmValues.Text(job, "id") is { Length: > 0 } id && !baseline.Contains(id, StringComparer.Ordinal)
            && started.HasValue && Timestamp(job, "createdAt") >= started).ToArray();
        // A new terminal job is still ambiguous if another new job is processing.
        return candidates.Length == 1 && GsmValues.Text(candidates[0], "status").ToUpperInvariant() is "SENT" or "FAILED" or "SKIPPED" ? null : pending;
    }
    private async Task TryPartAsync(Func<Task> action, Context context, CancellationToken ct)
    {
        try { await action(); }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        { Check(context, ct); SetError(error); }
    }
    private async Task<bool> RunAsync(Func<Context, CancellationToken, Task> action, bool requireOnline = false)
    {
        if (IsBusy) return false;
        if (app == null) return Fail("Войдите в LumaWork.");
        if (requireOnline && !online) return Fail("Для этого действия требуется подключение к LumaWork.");
        var context = new Context(revision, app, so, online, Month, CacheKey("gsm", app.User.Id, Month), so == null ? CacheKey("closed", app.User.Id, "manual") : CacheKey("closed", app.User.Id, "simpleone", so.User.Id));
        using var owned = new CancellationTokenSource(); operation = owned;
        ErrorMessage = null; InfoMessage = null; RequiresReauthentication = false; Changed?.Invoke();
        try { await action(context, owned.Token); Check(context, owned.Token); return ErrorMessage == null; }
        catch (OperationCanceledException) when (owned.IsCancellationRequested || revision != context.Revision) { return false; }
        catch (Exception error)
        {
            if (owned.IsCancellationRequested || revision != context.Revision) return false;
            SetError(error); return false;
        }
        finally { if (ReferenceEquals(operation, owned)) { operation = null; Changed?.Invoke(); } }
    }
    private void Check(Context context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (context.Revision != revision) throw new OperationCanceledException(ct);
    }
    private void ClearGsm()
    {
        ProfileResult = new(null, []); Projects = []; MonthData = new(Month, [], [], null); Jobs = [];
        LastGsmUpdated = null; pendingReport = null; gsmCacheLoaded = false;
    }
    private bool Fail(string message) { ErrorMessage = message; InfoMessage = null; Changed?.Invoke(); return false; }
    private void SetError(Exception error)
    {
        RequiresReauthentication |= error is AuthException { InvalidatesSession: true };
        ErrorMessage = error switch
        {
            AuthException or ArgumentException or InvalidOperationException or InvalidDataException => error.Message,
            IOException => "Не удалось прочитать или сохранить данные на компьютере. Проверьте хранилище и повторите.",
            HttpRequestException => "Нет связи с сервером. Сохранённые данные доступны; повторите обновление.",
            OperationCanceledException => "Сервер не ответил вовремя. Сохранённые данные не изменены.",
            _ => "Не удалось обработать данные. Сохранённые данные не изменены; повторите обновление."
        };
    }
    private string CacheKey(params string[] parts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { originScope }.Concat(parts))))).ToLowerInvariant();
    private static JsonNode? Node<T>(T value) => JsonSerializer.SerializeToNode(value, JsonOptions);
    private static T Read<T>(JsonObject document, string key) where T : class => document[key]?.Deserialize<T>(JsonOptions) ?? throw new InvalidDataException("Сохранённые данные повреждены. Обновите данные.");
    private static DateTimeOffset? Timestamp(JsonObject document, string key) => DateTimeOffset.TryParse(GsmValues.Text(document, key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value) ? value : null;
    private static void ScrubCredentials(JsonNode? node)
    {
        if (node is JsonObject document)
            foreach (var key in document.Select(pair => pair.Key).ToArray())
            {
                var normalized = key.Replace("_", "").Replace("-", "").ToLowerInvariant();
                if (normalized.Contains("password") || normalized.Contains("token") || normalized.Contains("cookie") || normalized is "auth" or "authorization" or "authkey") document.Remove(key);
                else ScrubCredentials(document[key]);
            }
        else if (node is JsonArray array) foreach (var child in array) ScrubCredentials(child);
    }
    private sealed record Context(long Revision, AppSession App, SimpleOneSession? SimpleOne, bool Online, string Month, string GsmKey, string ClosedKey);
}
