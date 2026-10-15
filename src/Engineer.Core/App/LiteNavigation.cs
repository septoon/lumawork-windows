namespace Engineer.Core.App;

public enum LiteSection
{
    Profile,
    Home,
    ClosedRequests
}

public sealed record LiteSectionDescriptor(LiteSection Section, string Key, string Title);

public static class LiteNavigation
{
    public static IReadOnlyList<LiteSectionDescriptor> Sections { get; } = Array.AsReadOnly(
        new[]
        {
            new LiteSectionDescriptor(LiteSection.Profile, "profile", "Профиль"),
            new LiteSectionDescriptor(LiteSection.ClosedRequests, "requests", "Заявки"),
            new LiteSectionDescriptor(LiteSection.Home, "home", "Месячный отчёт")
        });

    public static LiteSection Restore(string? savedKey) => savedKey switch
    {
        "profile" => LiteSection.Profile,
        "requests" => LiteSection.ClosedRequests,
        _ => LiteSection.Home
    };
}
