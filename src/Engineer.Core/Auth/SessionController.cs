using System.Net.Mail;

namespace Engineer.Core.Auth;

public enum ConnectionState { SignedOut, Online, Offline }

// Owned by the window's UI context. Each operation is cancelled before account changes.
public sealed class SessionController(ILumaWorkAuthApi luma, ISimpleOneAuthApi so, ISessionCredentialStore store, string appOrigin, string simpleOneOrigin)
{
    private CancellationTokenSource? operation;
    private SessionCredentials? retained;
    private int revision;
    public AppSession? App { get; private set; }
    public SimpleOneSession? SimpleOne { get; private set; }
    public string? CodeEmail { get; private set; }
    public string? Error { get; private set; }
    public string? SimpleOneError { get; private set; }
    public ConnectionState AppConnection { get; private set; }
    public ConnectionState SimpleOneConnection { get; private set; }
    public bool IsBusy => operation != null;
    public event Action? Changed;

    public async Task RestoreAsync() => await RunAsync(async ct =>
    {
        var saved = store.Read();
        if (saved == null) return;
        if (saved.AppOrigin.TrimEnd('/') != appOrigin.TrimEnd('/') || saved.SimpleOneOrigin.TrimEnd('/') != simpleOneOrigin.TrimEnd('/')
            || string.IsNullOrWhiteSpace(saved.App?.Token) || string.IsNullOrWhiteSpace(saved.App.User.Id))
        { store.Delete(); Error = "Параметры входа изменились. Войдите снова."; return; }
        retained = saved;
        try
        {
            var user = await luma.CurrentUserAsync(saved.App.Token, ct); ct.ThrowIfCancellationRequested();
            if (user.Id != saved.App.User.Id) throw new AuthException("Сохранённая сессия не соответствует пользователю. Войдите снова.", true);
            saved = saved with { App = saved.App with { User = user } };
            store.Write(saved); retained = saved; App = saved.App; AppConnection = ConnectionState.Online;
        }
        catch (AuthException e) when (e.InvalidatesSession) { ct.ThrowIfCancellationRequested(); ClearLocal(); Error = e.Message; return; }
        catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        { ct.ThrowIfCancellationRequested(); App = saved.App; AppConnection = ConnectionState.Offline; Error = "Нет связи с LumaWork. Сессия сохранена; проверьте подключение и повторите."; return; }
        if (saved.SimpleOne is not { } connected) return;
        try
        {
            var user = await so.CurrentUserAsync(connected.AuthKey, ct); ct.ThrowIfCancellationRequested();
            if (user.Id != connected.User.Id) throw new AuthException("Пользователь SimpleOne изменился. Подключитесь снова.", true);
            Save(saved.App, connected with { User = user }); SimpleOneConnection = ConnectionState.Online;
        }
        catch (AuthException e) when (e.InvalidatesSession) { ct.ThrowIfCancellationRequested(); Save(saved.App, null); SimpleOneError = e.Message; }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or AuthException)
        { ct.ThrowIfCancellationRequested(); SimpleOne = connected; SimpleOneConnection = ConnectionState.Offline; SimpleOneError = "Не удалось проверить SimpleOne. Сессия сохранена; повторите подключение."; }
    });

    public Task<bool> RequestCodeAsync(string email)
    {
        email = email.Trim().ToLowerInvariant();
        if (App != null || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !parsed.Host.Contains('.'))
        { Error = "Введите корректный рабочий email."; Changed?.Invoke(); return Task.FromResult(false); }
        return RunAsync(async ct => { await luma.RequestCodeAsync(email, ct); ct.ThrowIfCancellationRequested(); CodeEmail = email; });
    }
    public Task<bool> VerifyCodeAsync(string code)
    {
        code = code.Trim();
        if (CodeEmail == null || code.Length != 6 || code.Any(c => c < '0' || c > '9'))
        { Error = "Введите шестизначный код из письма."; Changed?.Invoke(); return Task.FromResult(false); }
        var email = CodeEmail;
        return RunAsync(async ct =>
        {
            var session = await luma.VerifyCodeAsync(email, code, ct); ct.ThrowIfCancellationRequested();
            Save(session, null); AppConnection = ConnectionState.Online; CodeEmail = null;
        });
    }
    public void ChangeEmail()
    {
        if (IsBusy) return;
        CodeEmail = null; Error = null; Changed?.Invoke();
    }
    public Task<bool> LoginSimpleOneAsync(string username, string password)
    {
        if (AppConnection != ConnectionState.Online || App == null) return Task.FromResult(false);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        { SimpleOneError = "Введите логин и пароль SimpleOne."; Changed?.Invoke(); return Task.FromResult(false); }
        var app = App;
        return RunAsync(async ct =>
        {
            var key = await so.LoginAsync(username.Trim(), password, ct); ct.ThrowIfCancellationRequested();
            var user = await so.CurrentUserAsync(key, ct); ct.ThrowIfCancellationRequested();
            Save(app, new(key, user)); SimpleOneConnection = ConnectionState.Online;
        }, true);
    }
    public void DisconnectSimpleOne()
    {
        if (IsBusy || App == null) return;
        try { Save(App, null); SimpleOneError = null; }
        catch { SimpleOneError = "Не удалось удалить сессию из хранилища Windows. Повторите выход."; }
        Changed?.Invoke();
    }
    public async Task LogoutAsync()
    {
        operation?.Cancel(); operation = null;
        var token = App?.Token ?? retained?.App.Token;
        try { ClearLocal(); }
        catch { Error = "Не удалось удалить сессию из хранилища Windows. Повторите выход."; Changed?.Invoke(); return; }
        var logoutRevision = ++revision;
        Changed?.Invoke();
        if (token == null) return;
        try { await luma.LogoutAsync(token); }
        catch { if (revision == logoutRevision) { Error = "На этом компьютере выполнен выход. Сервер не подтвердил завершение сессии."; Changed?.Invoke(); } }
    }
    public void CancelPending() { operation?.Cancel(); }
    private void Save(AppSession app, SimpleOneSession? connected)
    {
        var saved = new SessionCredentials(appOrigin, simpleOneOrigin, app, connected);
        store.Write(saved); retained = saved; App = app; SimpleOne = connected;
        if (connected == null) SimpleOneConnection = ConnectionState.SignedOut;
    }
    private void ClearLocal()
    {
        store.Delete(); retained = null; App = null; SimpleOne = null; CodeEmail = null;
        AppConnection = ConnectionState.SignedOut; SimpleOneConnection = ConnectionState.SignedOut; Error = null; SimpleOneError = null;
    }
    private async Task<bool> RunAsync(Func<CancellationToken, Task> action, bool simpleOne = false)
    {
        if (IsBusy) return false;
        using var owned = new CancellationTokenSource(); operation = owned; revision++;
        if (simpleOne) SimpleOneError = null; else Error = null;
        Changed?.Invoke();
        try { await action(owned.Token); owned.Token.ThrowIfCancellationRequested(); return true; }
        catch (OperationCanceledException) when (owned.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            // A handler may ignore cancellation; never publish its late result after logout.
            if (owned.IsCancellationRequested) return false;
            var message = error switch
            {
                AuthException => error.Message,
                HttpRequestException => "Не удалось подключиться к серверу. Проверьте интернет и повторите.",
                OperationCanceledException => "Сервер не ответил вовремя. Повторите попытку.",
                _ => "Не удалось прочитать или сохранить сессию в хранилище Windows. Повторите попытку."
            };
            if (simpleOne) SimpleOneError = message; else Error = message;
            return false;
        }
        finally { if (ReferenceEquals(operation, owned)) { operation = null; Changed?.Invoke(); } }
    }
}
