using System.Text.Json;
using Engineer.Core.App;
using Engineer.Core.Auth;

namespace Engineer.Lite.Windows.Platform;

internal static class SessionFactory
{
    internal static SessionController Create()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var packaged = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(path)) ?? [];
        var environment = new Dictionary<string, string?>();
        foreach (var key in new[] { "LUMAWORK_API_URL", "LUMAWORK_API_ORIGIN", "SIMPLEONE_API_URL", "SIMPLEONE_API_ORIGIN" })
            environment[key] = Environment.GetEnvironmentVariable(key);
        var luma = LiteConfiguration.ResolveLumaWork(environment, packaged, new Dictionary<string, string?>());
        // Reuse the same validation and per-key precedence for SimpleOne.
        static Dictionary<string, string?> SimpleOneKeys(IReadOnlyDictionary<string, string?> source) => new()
        {
            ["LUMAWORK_API_URL"] = source.GetValueOrDefault("SIMPLEONE_API_URL"),
            ["LUMAWORK_API_ORIGIN"] = source.GetValueOrDefault("SIMPLEONE_API_ORIGIN")
        };
        var so = LiteConfiguration.ResolveLumaWork(SimpleOneKeys(environment), SimpleOneKeys(packaged), new Dictionary<string, string?>());
        if (luma.Origin == null || so.Origin == null) throw new ArgumentException("Проверьте адреса LumaWork и SimpleOne в appsettings.json.");
        return new(new LumaWorkAuthApi(luma.Origin), new SimpleOneAuthApi(so.Origin), new WindowsSessionCredentialStore(), luma.Origin.AbsoluteUri, so.Origin.AbsoluteUri);
    }
}
