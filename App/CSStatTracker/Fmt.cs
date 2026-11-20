using System.Globalization;
using Microsoft.AspNetCore.Html;

namespace CSStatTracker;

/// <summary>Display formatting shared by the pages.</summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Num(decimal? value, string format = "0.00") =>
        value is null ? "—" : value.Value.ToString(format, Inv);

    public static string Pct(decimal? value) =>
        value is null ? "—" : value.Value.ToString("0.0", Inv) + "%";

    public static string Int(int value) => value.ToString("N0", Inv);

    public static string When(DateTime value) => value.ToString("MMM d, yyyy · h:mm tt", Inv);

    public static string Day(DateOnly value) => value.ToString("MMM d, yyyy", Inv);

    public static string Day(DateTime value) => value.ToString("MMM d, yyyy", Inv);

    public static string Iso(DateTime value) => value.ToString("yyyy-MM-dd", Inv);

    /// <summary>Premier CS Rating, shown like the game does ("18,250"), or "Unranked".</summary>
    public static string Premier(int? rating) => rating is null ? "Unranked" : rating.Value.ToString("N0", Inv);

    public static string Competitive(int? rank) => Data.CompetitiveRanks.Name(rank);

    /// <summary>Round length as m:ss.</summary>
    public static string Duration(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>Spaces out WinReason values: "BombDefused" -> "Bomb defused".</summary>
    public static string Reason(string winReason) => winReason switch
    {
        "BombExploded" => "Bomb exploded",
        "BombDefused" => "Bomb defused",
        "TimeExpired" => "Time ran out",
        "HostageRescued" => "Hostages rescued",
        _ => winReason
    };

    public static string Iso(DateOnly? value) => value?.ToString("yyyy-MM-dd", Inv) ?? "";

    /// <summary>
    /// A signed difference. Positive means the player is better, so it gets
    /// the "better" style; the sign and arrow carry the meaning without color.
    /// </summary>
    public static IHtmlContent Diff(decimal? value, string format = "0.00", string suffix = "")
    {
        if (value is null)
        {
            return new HtmlString("<span class=\"diff none\">—</span>");
        }

        var v = value.Value;
        var text = Math.Abs(v).ToString(format, Inv) + suffix;
        return v switch
        {
            > 0 => new HtmlString($"<span class=\"diff pos\" title=\"You're ahead\">▲ +{text}</span>"),
            < 0 => new HtmlString($"<span class=\"diff neg\" title=\"Your friend is ahead\">▼ −{text}</span>"),
            _ => new HtmlString("<span class=\"diff even\">even</span>")
        };
    }

    /// <summary>Result is stored as W / L / T.</summary>
    public static string ResultText(string? result) => result switch
    {
        "W" => "Win",
        "L" => "Loss",
        "T" => "Tie",
        _ => ""
    };

    public static string ResultClass(string? result) => result switch
    {
        "W" => "badge win",
        "L" => "badge loss",
        "T" => "badge tie",
        _ => "badge"
    };
}
