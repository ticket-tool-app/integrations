using TicketTool.PluginSdk;

namespace TicketToolLink;

/// <summary>The parts of actions and reports that don't need a running server.</summary>
internal static class GameText
{
    /// <summary>The configured actions in protocol order, without unknown or repeated kinds.</summary>
    public static List<string> EnabledActions(IEnumerable<string?>? configured, List<string> problems)
    {
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? item in configured ?? Array.Empty<string?>())
        {
            string kind = (item ?? "").Trim().ToLowerInvariant();
            if (ActionRequest.Kinds.Contains(kind)) listed.Add(kind);
            else problems.Add("actions: \"" + item + "\" isn't one of message, warn, kick, unban; ignoring it");
        }
        return ActionRequest.Kinds.Where(listed.Contains).ToList();
    }

    /// <summary>Text that goes inside a quoted console argument: no quotes, separators or control characters.</summary>
    public static string ConsoleArg(string? text)
    {
        string clean = Text.StripFormatting(text ?? "") ?? "";
        clean = new string(clean.Where(c => c != '"' && c != ';' && c != '\'' && !char.IsControl(c)).ToArray());
        return Text.Clip(clean.Trim(), 200) ?? "";
    }

    /// <summary>
    /// Splits "[player] message": the first word names the target only when it
    /// matches an online player and more words follow.
    /// </summary>
    public static (string? Target, string Message) SplitReport(IReadOnlyList<string> args, Func<string, string?> findPlayer)
    {
        string? target = args.Count > 1 ? findPlayer(args[0]) : null;
        string message = string.Join(" ", args.Skip(target == null ? 0 : 1)).Trim();
        return (target, message);
    }

    public static bool IsCommandName(string s) =>
        s.Length is > 0 and <= 32 && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    public static string ReportReply(ReportOutcome outcome) => outcome switch
    {
        ReportOutcome.Opened => "Thanks! Your report reached the staff team.",
        ReportOutcome.Ignored => "The staff team isn't taking reports this way right now. Try their Discord server instead.",
        ReportOutcome.RateLimited => "Lots of reports are coming in right now. Try again in a minute.",
        ReportOutcome.NotEnabled => "Reports aren't turned on for this server.",
        _ => "Your report couldn't be sent. Try again in a minute.",
    };
}
