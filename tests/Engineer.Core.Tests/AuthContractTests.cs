using System.Net;
using System.Text;
using System.Text.Json;
using Engineer.Core.Auth;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class AuthContractTests
{
    [Fact]
    public async Task EmailCodeMeAndLogoutUseExistingContract()
    {
        var calls = new List<string>();
        using var http = Client(async r =>
        {
            calls.Add(r.RequestUri!.AbsolutePath);
            Assert.Null(r.Headers.Contains("Cookie") ? "unexpected" : null);
            if (r.Method == HttpMethod.Post && r.RequestUri.AbsolutePath != "/auth/logout")
            {
                Assert.Null(r.Headers.Authorization);
                var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync()).RootElement;
                Assert.Equal("engineer@example.invalid", body.GetProperty("email").GetString());
                if (r.RequestUri.AbsolutePath.EndsWith("verify-code")) Assert.Equal("000000", body.GetProperty("code").GetString());
            }
            else Assert.Equal("Bearer synthetic-token", r.Headers.Authorization?.ToString());
            return r.RequestUri.AbsolutePath switch
            {
                "/auth/verify-code" => Response(200, """{"token":"synthetic-token","user":{"id":"a","email":"engineer@example.invalid","role":"engineer"}}"""),
                "/me" => Response(200, """{"user":{"id":"a","email":"engineer@example.invalid","role":"engineer"}}"""),
                _ => Response(204, "")
            };
        });
        var api = new LumaWorkAuthApi(new Uri("https://luma.example.invalid"), http);
        await api.RequestCodeAsync("engineer@example.invalid");
        var session = await api.VerifyCodeAsync("engineer@example.invalid", "000000");
        Assert.Equal("a", (await api.CurrentUserAsync(session.Token)).Id);
        await api.LogoutAsync(session.Token);
        Assert.Equal(new[] { "/auth/request-code", "/auth/verify-code", "/me", "/auth/logout" }, calls);
        Assert.DoesNotContain("synthetic-token", session.ToString());
    }

    [Fact]
    public async Task SimpleOneUsesV1AndOnlyItsOwnExplicitCookie()
    {
        using var http = Client(async r =>
        {
            Assert.Null(r.Headers.Authorization);
            Assert.Equal("no-cache", r.Headers.CacheControl?.ToString());
            if (r.RequestUri!.AbsolutePath == "/v1/auth/login")
            {
                Assert.False(r.Headers.Contains("Cookie"));
                var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync()).RootElement;
                Assert.Equal("fixture.engineer", body.GetProperty("username").GetString());
                Assert.Equal("synthetic-password", body.GetProperty("password").GetString());
                Assert.Equal("ru", body.GetProperty("language").GetString());
                return Response(200, """{"status":"OK","data":{"auth_key":"synthetic-cookie"}}""");
            }
            Assert.Equal("/v1/user/me", r.RequestUri.AbsolutePath);
            Assert.Equal("auth=synthetic-cookie", Assert.Single(r.Headers.GetValues("Cookie")));
            return Response(200, """{"status":"OK","data":{"sys_id":"0000123","username":"fixture.engineer","first_name":"Иван","last_name":"Тестов"}}""");
        });
        var api = new SimpleOneAuthApi(new Uri("https://so.example.invalid/v1"), http);
        var key = await api.LoginAsync("fixture.engineer", "synthetic-password");
        var user = await api.CurrentUserAsync(key);
        Assert.Equal("0000123", user.Id);
        Assert.Equal("Иван Тестов", user.DisplayName);
    }

    [Theory]
    [InlineData(400, "{\"error\":\"INVALID_CODE\"}", "Код не подошёл или истёк.", false)]
    [InlineData(403, "{\"error\":\"USER_BLOCKED\"}", "Доступ отозван. Обратитесь в поддержку.", true)]
    [InlineData(429, "{\"error\":\"CODE_RECENTLY_SENT\"}", "Код уже отправлен. Повторите через минуту.", false)]
    public async Task LumaErrorsKeepActionableMeaning(int status, string body, string expected, bool invalidates)
    {
        using var http = Client(_ => Task.FromResult(Response(status, body)));
        var error = await Assert.ThrowsAsync<AuthException>(() => new LumaWorkAuthApi(new Uri("https://luma.example.invalid"), http).VerifyCodeAsync("a@b.invalid", "000000"));
        Assert.Equal(expected, error.Message);
        Assert.Equal(invalidates, error.InvalidatesSession);
    }

    [Theory]
    [InlineData(500, "{\"status\":\"ERROR\",\"errors\":[{\"message\":\"Wrong username or password\"}]}")]
    [InlineData(401, "{}")]
    public async Task SimpleOneCredentialRefusalDoesNotExposePassword(int status, string body)
    {
        using var http = Client(_ => Task.FromResult(Response(status, body)));
        var error = await Assert.ThrowsAsync<AuthException>(() => new SimpleOneAuthApi(new Uri("https://so.example.invalid/v1"), http).LoginAsync("a", "synthetic-password"));
        Assert.Equal("Неверный логин или пароль SimpleOne.", error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"token\":\"\",\"user\":{\"id\":\"a\"}}")]
    public async Task MalformedSessionsAreRejected(string body)
    {
        using var http = Client(_ => Task.FromResult(Response(200, body)));
        await Assert.ThrowsAsync<AuthException>(() => new LumaWorkAuthApi(new Uri("https://luma.example.invalid"), http).VerifyCodeAsync("a@b.invalid", "000000"));
    }

    [Fact]
    public void NonLocalPlainHttpIsRejectedBeforeSendingSecrets()
    {
        Assert.Throws<ArgumentException>(() => new LumaWorkAuthApi(new Uri("http://luma.example.invalid")));
        Assert.Throws<ArgumentException>(() => new SimpleOneAuthApi(new Uri("http://so.example.invalid/v1")));
    }

    internal static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => new(new Handler(handler));
    internal static HttpResponseMessage Response(int status, string body) => new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
