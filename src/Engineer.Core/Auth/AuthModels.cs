namespace Engineer.Core.Auth;

public sealed record AppUser(string Id, string Email, string Role);
public sealed record AppSession(string Token, AppUser User)
{
    public override string ToString() => $"LumaWork session ({User.Id})";
}
public sealed record SimpleOneUser(string Id, string Username, string FirstName, string LastName)
{
    public string DisplayName => string.IsNullOrWhiteSpace($"{FirstName} {LastName}".Trim()) ? Username : $"{FirstName} {LastName}".Trim();
}
public sealed record SimpleOneSession(string AuthKey, SimpleOneUser User)
{
    public override string ToString() => $"SimpleOne session ({User.Id})";
}
public sealed record SessionCredentials(string AppOrigin, string SimpleOneOrigin, AppSession App, SimpleOneSession? SimpleOne)
{
    public override string ToString() => "Protected session credentials";
}
public sealed class AuthException(string message, bool invalidatesSession = false) : Exception(message)
{
    public bool InvalidatesSession { get; } = invalidatesSession;
}
public interface ILumaWorkAuthApi
{
    Task RequestCodeAsync(string email, CancellationToken cancellationToken = default);
    Task<AppSession> VerifyCodeAsync(string email, string code, CancellationToken cancellationToken = default);
    Task<AppUser> CurrentUserAsync(string token, CancellationToken cancellationToken = default);
    Task LogoutAsync(string token, CancellationToken cancellationToken = default);
}
public interface ISimpleOneAuthApi
{
    Task<string> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<SimpleOneUser> CurrentUserAsync(string authKey, CancellationToken cancellationToken = default);
}
public interface ISessionCredentialStore
{
    SessionCredentials? Read();
    void Write(SessionCredentials credentials);
    void Delete();
}
