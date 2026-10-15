namespace Engineer.Core.Auth;

public sealed class SimpleOneAuthApi : ISimpleOneAuthApi
{
    private readonly AuthTransport transport;
    public SimpleOneAuthApi(Uri origin, HttpClient? client = null) => transport = new(origin, client, true);
    public async Task<string> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var json = await transport.SendAsync("auth/login", HttpMethod.Post, new { username, password, language = "ru" }, null, cancellationToken);
        return AuthTransport.Required(AuthTransport.Object(json, "data"), "auth_key");
    }
    public async Task<SimpleOneUser> CurrentUserAsync(string authKey, CancellationToken cancellationToken = default)
    {
        var json = AuthTransport.Object(await transport.SendAsync("user/me", HttpMethod.Get, null, authKey, cancellationToken), "data");
        return new(AuthTransport.Required(json, "sys_id"), AuthTransport.Text(json, "username") ?? "", AuthTransport.Text(json, "first_name") ?? "", AuthTransport.Text(json, "last_name") ?? "");
    }
}
