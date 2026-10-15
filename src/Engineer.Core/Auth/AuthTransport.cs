using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Engineer.Core.Auth;

internal sealed class AuthTransport
{
    private readonly Uri origin;
    private readonly HttpClient client;
    private readonly bool simpleOne;
    internal AuthTransport(Uri origin, HttpClient? client, bool simpleOne)
    {
        if (!origin.IsAbsoluteUri || (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback))
            || !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
            throw new ArgumentException("Для входа требуется корректный HTTPS-адрес сервера.");
        this.origin = new Uri(origin.AbsoluteUri.TrimEnd('/') + "/");
        this.simpleOne = simpleOne;
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(simpleOne ? 25 : 45) };
    }

    internal async Task<JsonElement> SendAsync(string path, HttpMethod method, object? body, string? credential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(origin, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        if (simpleOne)
        {
            if (credential != null)
            {
                if (credential.Any(c => char.IsControl(c) || c is ';' or ',' or ' ')) throw InvalidResponse();
                request.Headers.Add("Cookie", "auth=" + credential);
            }
        }
        else
        {
            request.Headers.Add("X-LumaWork-Version", "0.1.0");
            request.Headers.Add("X-LumaWork-Build", "3");
            if (credential != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }
        if (body != null) request.Content = JsonContent.Create(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(path == "me" ? 8 : simpleOne ? 25 : 45));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        // No request/response bodies or credentials are written to diagnostics.
        JsonElement json = default;
        try
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (bytes.Length > 0) { using var document = JsonDocument.Parse(bytes); json = document.RootElement.Clone(); }
        }
        catch (JsonException) { if (response.IsSuccessStatusCode) throw InvalidResponse(); }
        if (simpleOne)
        {
            var message = Text(json, "message") ?? Text(json, "error");
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                message ??= errors.EnumerateArray().Select(e => Text(e, "message")).FirstOrDefault(m => !string.IsNullOrEmpty(m));
            var refused = message?.Contains("credentials", StringComparison.OrdinalIgnoreCase) == true
                || message?.Contains("wrong username or password", StringComparison.OrdinalIgnoreCase) == true;
            if (response.StatusCode == HttpStatusCode.Unauthorized || refused)
                throw new AuthException(path == "auth/login" ? "Неверный логин или пароль SimpleOne." : "Сессия SimpleOne истекла. Войдите снова.", path != "auth/login");
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new AuthException("SimpleOne запретил доступ (HTTP 403).");
            if (!response.IsSuccessStatusCode) throw new AuthException($"SimpleOne недоступен (HTTP {(int)response.StatusCode}). Повторите позже.");
            if (Text(json, "status") == "ERROR" || message != null) throw new AuthException("SimpleOne не выполнил запрос. Повторите позже.");
        }
        else if (!response.IsSuccessStatusCode)
        {
            throw Text(json, "error") switch
            {
                "INVALID_CODE" => new AuthException("Код не подошёл или истёк."),
                "CODE_RECENTLY_SENT" => new AuthException("Код уже отправлен. Повторите через минуту."),
                "USER_BLOCKED" => new AuthException("Доступ отозван. Обратитесь в поддержку.", true),
                _ when response.StatusCode is HttpStatusCode.Unauthorized => new AuthException("Сессия LumaWork истекла. Войдите снова.", true),
                _ => new AuthException($"LumaWork вернул HTTP {(int)response.StatusCode}. Повторите позже.")
            };
        }
        return json;
    }
    internal static string? Text(JsonElement json, string name) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value)
        ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null : null;
    internal static JsonElement Object(JsonElement json, string name) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw InvalidResponse();
    internal static string Required(JsonElement json, string name) => Text(json, name) is { } text && !string.IsNullOrWhiteSpace(text) ? text : throw InvalidResponse();
    internal static AuthException InvalidResponse() => new("Сервер вернул некорректный ответ. Повторите позже.");
}
