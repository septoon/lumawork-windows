using System.Text.Json;
using Engineer.Core.App;
using Engineer.Core.Auth;
using Engineer.Core.Gsm;
using Engineer.Core.ClosedRequests;
using Engineer.Core.Workspace;

namespace Engineer.Lite.Windows.Platform;

internal static class SessionFactory
{
    internal static SessionController Create()
    {
        var (luma,so,_)=Configuration();
        return new(new LumaWorkAuthApi(luma),new SimpleOneAuthApi(so),new WindowsSessionCredentialStore(),luma.AbsoluteUri,so.AbsoluteUri);
    }
    internal static WorkspaceController CreateWorkspace()
    {
        var (luma,so,settings)=Configuration();
        string Setting(string key)=>Environment.GetEnvironmentVariable(key)??settings.GetValueOrDefault(key)??"";
        var query=new SimpleOneClosedRequestsQuery(Setting("SIMPLEONE_CURRENT_USER_DYNAMIC_ID"),Setting("SIMPLEONE_ASSIGNED_USER_DYNAMIC_ID"),Setting("SIMPLEONE_RESOLVED_DATE_OPTION_ID"));
        return new(new GsmApi(luma),new SimpleOneClosedRequestsApi(so,query:query),new WindowsWorkspaceStore(),luma.AbsoluteUri+"|"+so.AbsoluteUri);
    }
    private static (Uri Luma,Uri So,Dictionary<string,string?> Settings) Configuration()
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
        return (luma.Origin,so.Origin,packaged);
    }
}
