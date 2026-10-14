using Engineer.Core.App;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void EnvironmentOverridesPackagedConfigForTheSameKey()
    {
        var endpoint = LiteConfiguration.ResolveLumaWork(
            Values(("LUMAWORK_API_URL", " https://environment.example/api ")),
            Values(("LUMAWORK_API_URL", "https://packaged.example")),
            Values(("LUMAWORK_API_URL", "https://preferences.example")));

        Assert.Equal(EndpointState.Ready, endpoint.State);
        Assert.Equal("https://environment.example/api", endpoint.Origin!.AbsoluteUri);
    }

    [Fact]
    public void PrimaryKeyKeepsExistingPrecedenceOverEnvironmentAlias()
    {
        var endpoint = LiteConfiguration.ResolveLumaWork(
            Values(("LUMAWORK_API_ORIGIN", "https://alias.example")),
            Values(("LUMAWORK_API_URL", "https://primary.example")),
            Values());

        Assert.Equal("primary.example", endpoint.Origin!.Host);
    }

    [Fact]
    public void PlaceholderAndBlankValuesFallThroughToConfiguredAlias()
    {
        var endpoint = LiteConfiguration.ResolveLumaWork(
            Values(("LUMAWORK_API_URL", "$(LUMAWORK_API_URL)")),
            Values(("LUMAWORK_API_URL", "  ")),
            Values(("LUMAWORK_API_ORIGIN", "https://preferences.example")));

        Assert.Equal("preferences.example", endpoint.Origin!.Host);
    }

    [Theory]
    [InlineData("file:///tmp/api")]
    [InlineData("/api")]
    [InlineData("https://user:secret@api.example")]
    [InlineData("https://api.example?token=secret")]
    [InlineData("https://api.example/#token")]
    [InlineData("not-a-url")]
    public void InvalidOriginIsAConfigurationError_NotAFileOrFallbackRequest(string raw)
    {
        var endpoint = LiteConfiguration.ResolveLumaWork(
            Values(("LUMAWORK_API_URL", raw)),
            Values(("LUMAWORK_API_URL", "https://fallback.example")),
            Values());

        Assert.Equal(EndpointState.Invalid, endpoint.State);
        Assert.Null(endpoint.Origin);
    }

    [Fact]
    public void MissingOriginStaysMissing()
    {
        var endpoint = LiteConfiguration.ResolveLumaWork(Values(), Values(), Values());
        Assert.Equal(EndpointState.Missing, endpoint.State);
        Assert.Null(endpoint.Origin);
    }

    private static IReadOnlyDictionary<string, string?> Values(params (string Key, string? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => item.Value);
}
