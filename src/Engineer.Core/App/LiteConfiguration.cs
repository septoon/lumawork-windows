namespace Engineer.Core.App;

public enum EndpointState
{
    Missing,
    Invalid,
    Ready
}

public sealed record EndpointConfiguration(EndpointState State, Uri? Origin);

public static class LiteConfiguration
{
    public static EndpointConfiguration ResolveLumaWork(
        IReadOnlyDictionary<string, string?> environment,
        IReadOnlyDictionary<string, string?> packaged,
        IReadOnlyDictionary<string, string?> preferences)
    {
        // Keep the source's per-key precedence: primary key's three sources,
        // then the alias's three sources. A configured invalid value is an error.
        foreach (var key in new[] { "LUMAWORK_API_URL", "LUMAWORK_API_ORIGIN" })
        {
            foreach (var source in new[] { environment, packaged, preferences })
            {
                if (!source.TryGetValue(key, out var value))
                    continue;

                var candidate = value?.Trim();
                if (string.IsNullOrEmpty(candidate) || candidate.StartsWith("$(", StringComparison.Ordinal))
                    continue;

                if (Uri.TryCreate(candidate, UriKind.Absolute, out var origin)
                    && origin.Scheme is "http" or "https"
                    && !string.IsNullOrEmpty(origin.Host)
                    && string.IsNullOrEmpty(origin.UserInfo)
                    && string.IsNullOrEmpty(origin.Query)
                    && string.IsNullOrEmpty(origin.Fragment))
                    return new(EndpointState.Ready, origin);

                return new(EndpointState.Invalid, null);
            }
        }

        return new(EndpointState.Missing, null);
    }
}
