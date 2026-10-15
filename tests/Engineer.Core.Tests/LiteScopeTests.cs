using Engineer.Core.App;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class LiteScopeTests
{
    [Fact]
    public void LiteScope_SeparatesProfileRequestsAndMonthlyReport()
    {
        Assert.Collection(LiteNavigation.Sections,
            profile => Assert.Equal(("profile", "Профиль"), (profile.Key, profile.Title)),
            requests => Assert.Equal(("requests", "Заявки"), (requests.Key, requests.Title)),
            home => Assert.Equal(("home", "Месячный отчёт"), (home.Key, home.Title)));

        Assert.Equal(LiteSection.Profile, LiteNavigation.Restore("profile"));
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
