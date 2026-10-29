using TicketTool.PluginSdk;

namespace TicketToolLink.Tests;

public sealed class GameTextTests
{
    [Fact]
    public void KeepsKnownActionsInProtocolOrder()
    {
        var problems = new List<string>();
        var on = GameText.EnabledActions(new[] { "Kick", "message", "slay", "kick", null }, problems);
        Assert.Equal(new[] { "message", "kick" }, on);
        Assert.Equal(2, problems.Count);
        Assert.Empty(GameText.EnabledActions(null, problems));
    }

    [Fact]
    public void ConsoleArgDropsQuotesSeparatorsAndControlCharacters()
    {
        Assert.Equal("bye quit everyone", GameText.ConsoleArg("bye\"; quit\u0002 'everyone'"));
        Assert.Equal(200, GameText.ConsoleArg(new string('a', 300)).Length);
    }

    [Fact]
    public void FirstWordIsTheTargetOnlyWhenItIsAnOnlinePlayer()
    {
        Func<string, string?> find = n => n.Equals("flyer", StringComparison.OrdinalIgnoreCase) ? "Flyer" : null;
        Assert.Equal(("Flyer", "is flying"), GameText.SplitReport(new[] { "flyer", "is", "flying" }, find));
        Assert.Equal(((string?)null, "server lag"), GameText.SplitReport(new[] { "server", "lag" }, find));
        Assert.Equal(((string?)null, "flyer"), GameText.SplitReport(new[] { "flyer" }, find));
    }

    [Fact]
    public void ValidatesCommandNames()
    {
        Assert.True(GameText.IsCommandName("report"));
        Assert.False(GameText.IsCommandName("re port"));
        Assert.False(GameText.IsCommandName(""));
        Assert.Contains("reached", GameText.ReportReply(ReportOutcome.Opened));
    }
}
