namespace Engineer.Core.App;

public enum LiteSection
{
    Home,
    ClosedRequests
}

public sealed record LiteSectionDescriptor(LiteSection Section, string Key, string Title);

public static class LiteNavigation
{
    public static IReadOnlyList<LiteSectionDescriptor> Sections { get; } = Array.AsReadOnly(
        new[]
        {
            new LiteSectionDescriptor(LiteSection.Home, "home", "Главная"),
            new LiteSectionDescriptor(LiteSection.ClosedRequests, "requests", "Заявки")
        });

    public static LiteSection Restore(string? savedKey) => savedKey switch
    {
        "requests" => LiteSection.ClosedRequests,
        _ => LiteSection.Home
    };
}
