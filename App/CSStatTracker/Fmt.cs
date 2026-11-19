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

    public static string ResultClass(string? result) => result switch
    {
        "Win" => "badge win",
        "Loss" => "badge loss",
        "Tie" => "badge tie",
        _ => "badge"
    };
}
