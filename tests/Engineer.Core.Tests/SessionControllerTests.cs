using Engineer.Core.Auth;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class SessionControllerTests
{
    private const string AppOrigin = "https://luma.example.invalid";
    private const string SoOrigin = "https://so.example.invalid/v1";
    private static readonly AppUser User = new("a", "engineer@example.invalid", "engineer");
    private static SessionCredentials Saved() => new(AppOrigin, SoOrigin, new("synthetic-token", User), new("synthetic-cookie", new("s", "fixture", "Иван", "Тестов")));

    [Fact]
    public async Task LoginSavesOnlyVerifiedIdentitiesAndLogoutClearsBoth()
    {
        var store = new Store(); var api = new Luma();
        var session = new SessionController(api, new So(), store, AppOrigin, SoOrigin);
        Assert.True(await session.RequestCodeAsync(" ENGINEER@example.invalid "));
        Assert.Equal("engineer@example.invalid", session.CodeEmail);
        Assert.True(await session.VerifyCodeAsync("000000"));
        Assert.Equal("a", session.App?.User.Id);
        Assert.Null(store.Value?.SimpleOne);
        Assert.True(await session.LoginSimpleOneAsync("fixture", "synthetic-password"));
        Assert.Equal("s", store.Value?.SimpleOne?.User.Id);
        await session.LogoutAsync();
        Assert.Null(session.App); Assert.Null(session.SimpleOne); Assert.Null(store.Value);
        Assert.Equal(1, api.Logouts);
    }
    [Fact]
    public async Task OriginChangesNeverSendStoredTokenToAnotherServer()
    {
        var store = new Store { Value = Saved() }; var api = new Luma();
        var session = new SessionController(api, new So(), store, "https://other.example.invalid", SoOrigin);
        await session.RestoreAsync();
        Assert.Null(session.App); Assert.Null(store.Value); Assert.Equal(0, api.MeCalls);
    }
    [Fact]
    public async Task RestoreExpiredLumaClearsSimpleOneToo()
    {
        var store = new Store { Value = Saved() };
        var session = new SessionController(new Luma { Me = _ => throw new AuthException("expired", true) }, new So(), store, AppOrigin, SoOrigin);
        await session.RestoreAsync();
        Assert.Null(session.App); Assert.Null(session.SimpleOne); Assert.Null(store.Value);
    }
    [Fact]
    public async Task RestoreNetworkFailureKeepsCredentialsForRetry()
    {
        var store = new Store { Value = Saved() }; var api = new Luma { Me = _ => throw new HttpRequestException() };
        var session = new SessionController(api, new So(), store, AppOrigin, SoOrigin);
        await session.RestoreAsync();
        Assert.NotNull(store.Value); Assert.Equal(ConnectionState.Offline, session.AppConnection);
        Assert.False(await session.LoginSimpleOneAsync("fixture", "synthetic-password"));
        api.Me = _ => Task.FromResult(User);
        await session.RestoreAsync();
        Assert.Equal(ConnectionState.Online, session.AppConnection); Assert.Equal(ConnectionState.Online, session.SimpleOneConnection);
    }
    [Fact]
    public async Task LateRestoreCannotResurrectLoggedOutAccount()
    {
        var pending = new TaskCompletionSource<AppUser>();
        var store = new Store { Value = Saved() }; var api = new Luma { Me = _ => pending.Task };
        var session = new SessionController(api, new So(), store, AppOrigin, SoOrigin);
        var restoring = session.RestoreAsync();
        await session.LogoutAsync();
        pending.SetResult(User); await restoring;
        Assert.Null(store.Value); Assert.Null(session.App); Assert.Null(session.SimpleOne);
    }
    [Fact]
    public async Task ChangingUserOnRestoreIsRejected()
    {
        var store = new Store { Value = Saved() };
        var session = new SessionController(new Luma { Me = _ => Task.FromResult(User with { Id = "b" }) }, new So(), store, AppOrigin, SoOrigin);
        await session.RestoreAsync(); Assert.Null(session.App); Assert.Null(store.Value);
    }
    [Fact]
    public async Task SimpleOneExpiredDoesNotLogOutLuma()
    {
        var store = new Store { Value = Saved() };
        var session = new SessionController(new Luma(), new So { Expired = true }, store, AppOrigin, SoOrigin);
        await session.RestoreAsync();
        Assert.NotNull(session.App); Assert.Null(session.SimpleOne); Assert.Null(store.Value?.SimpleOne);
    }
    [Fact]
    public async Task PersistenceFailureDoesNotPretendThatLoginIsSaved()
    {
        var session = new SessionController(new Luma(), new So(), new Store { FailWrite = true }, AppOrigin, SoOrigin);
        await session.RequestCodeAsync(User.Email);
        Assert.False(await session.VerifyCodeAsync("000000")); Assert.Null(session.App); Assert.NotNull(session.Error);
    }
    [Fact]
    public async Task EmptyInputNeverCallsServer()
    {
        var api = new Luma(); var session = new SessionController(api, new So(), new Store(), AppOrigin, SoOrigin);
        Assert.False(await session.RequestCodeAsync("bad-email")); Assert.False(await session.VerifyCodeAsync("abc")); Assert.Equal(0, api.Codes);
    }
    [Fact]
    public async Task LateVerificationCannotPublishAfterLogout()
    {
        var pending = new TaskCompletionSource<AppSession>();
        var store = new Store(); var api = new Luma { Verification = pending.Task };
        var session = new SessionController(api, new So(), store, AppOrigin, SoOrigin);
        await session.RequestCodeAsync(User.Email);
        var verifying = session.VerifyCodeAsync("000000");
        await session.LogoutAsync();
        pending.SetResult(new AppSession("synthetic-token", User));
        Assert.False(await verifying); Assert.Null(session.App); Assert.Null(store.Value);
    }
    [Fact]
    public async Task LateSimpleOneCannotAttachToLoggedOutAccount()
    {
        var pending = new TaskCompletionSource<SimpleOneUser>();
        var store = new Store();
        var session = new SessionController(new Luma(), new So { Pending = pending.Task }, store, AppOrigin, SoOrigin);
        await session.RequestCodeAsync(User.Email); await session.VerifyCodeAsync("000000");
        var connecting = session.LoginSimpleOneAsync("fixture", "synthetic-password");
        await session.LogoutAsync();
        pending.SetResult(new SimpleOneUser("s", "fixture", "Иван", "Тестов"));
        Assert.False(await connecting); Assert.Null(session.SimpleOne); Assert.Null(store.Value);
    }
    private sealed class Store : ISessionCredentialStore
    {
        public SessionCredentials? Value; public bool FailWrite;
        public SessionCredentials? Read() => Value;
        public void Write(SessionCredentials value) { if (FailWrite) throw new IOException(); Value = value; }
        public void Delete() => Value = null;
    }
    private sealed class Luma : ILumaWorkAuthApi
    {
        public Task<AppSession>? Verification;
        public Func<CancellationToken, Task<AppUser>> Me = _ => Task.FromResult(User); public int MeCalls, Logouts, Codes;
        public Task RequestCodeAsync(string email, CancellationToken cancellationToken = default) { Codes++; return Task.CompletedTask; }
        public Task<AppSession> VerifyCodeAsync(string email, string code, CancellationToken cancellationToken = default) => Verification ?? Task.FromResult(new AppSession("synthetic-token", User));
        public Task<AppUser> CurrentUserAsync(string token, CancellationToken cancellationToken = default) { MeCalls++; return Me(cancellationToken); }
        public Task LogoutAsync(string token, CancellationToken cancellationToken = default) { Logouts++; return Task.CompletedTask; }
    }
    private sealed class So : ISimpleOneAuthApi
    {
        public bool Expired; public Task<SimpleOneUser>? Pending;
        public Task<string> LoginAsync(string username, string password, CancellationToken cancellationToken = default) => Task.FromResult("synthetic-cookie");
        public Task<SimpleOneUser> CurrentUserAsync(string authKey, CancellationToken cancellationToken = default) => Pending ?? (Expired ? throw new AuthException("expired", true) : Task.FromResult(new SimpleOneUser("s", "fixture", "Иван", "Тестов")));
    }
}
