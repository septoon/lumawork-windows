using Engineer.Core.App;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class LiteScopeTests
{
    [Fact]
    public void LiteScope_HasOnlyHomeAndClosedRequests()
    {
        Assert.Collection(LiteNavigation.Sections,
            home => Assert.Equal(("home", "Главная"), (home.Key, home.Title)),
            requests => Assert.Equal(("requests", "Заявки"), (requests.Key, requests.Title)));

        Assert.Equal(LiteSection.Home, LiteNavigation.Restore("home"));
        Assert.Equal(LiteSection.ClosedRequests, LiteNavigation.Restore("requests"));
    }

    [Theory]
    [InlineData("backpack")]
    [InlineData("employees")]
    [InlineData("maintenance")]
    [InlineData("fuel")]
    [InlineData("wiki")]
    [InlineData("ftp")]
    [InlineData("salary")]
    [InlineData("coordination")]
    [InlineData("timeReport")]
    [InlineData("analytics")]
    [InlineData("users")]
    [InlineData("unknown")]
    [InlineData(null)]
    public void RestoringExcludedOrUnknownSection_ReturnsHome(string? savedKey)
    {
        Assert.Equal(LiteSection.Home, LiteNavigation.Restore(savedKey));
    }
}
