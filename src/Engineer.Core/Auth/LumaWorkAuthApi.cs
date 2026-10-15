using System.Text.Json;

namespace Engineer.Core.Auth;

public sealed class LumaWorkAuthApi : ILumaWorkAuthApi
{
    private readonly AuthTransport transport;
    public LumaWorkAuthApi(Uri origin, HttpClient? client = null) => transport = new(origin, client, false);
    public async Task RequestCodeAsync(string email, CancellationToken cancellationToken = default) =>
        _ = await transport.SendAsync("auth/request-code", HttpMethod.Post, new { email }, null, cancellationToken);
    public async Task<AppSession> VerifyCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        var json = await transport.SendAsync("auth/verify-code", HttpMethod.Post, new { email, code }, null, cancellationToken);
        return new(AuthTransport.Required(json, "token"), User(AuthTransport.Object(json, "user")));
    }
    public async Task<AppUser> CurrentUserAsync(string token, CancellationToken cancellationToken = default) =>
        User(AuthTransport.Object(await transport.SendAsync("me", HttpMethod.Get, null, token, cancellationToken), "user"));
    public async Task LogoutAsync(string token, CancellationToken cancellationToken = default) =>
        _ = await transport.SendAsync("auth/logout", HttpMethod.Post, null, token, cancellationToken);
    private static AppUser User(JsonElement json) => new(AuthTransport.Required(json, "id"), AuthTransport.Required(json, "email"), AuthTransport.Text(json, "role") ?? "engineer");
}
