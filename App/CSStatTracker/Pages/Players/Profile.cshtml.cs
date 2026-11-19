using System.Globalization;
using System.Text.Json;
using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Players;

/// <summary>
/// A player's page. Uses three of the aggregating queries:
/// Report_PlayerTotals, Report_PlayerMapStats and Report_PlayerMonthlyTrend.
/// </summary>
public class ProfileModel : PageModel
{
    private readonly PlayerRepository _players;
    private readonly ReportRepository _reports;

    public ProfileModel(PlayerRepository players, ReportRepository reports)
    {
        _players = players;
        _reports = reports;
    }

    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }

    public Player Player { get; private set; } = null!;
    public PlayerTotals? Totals { get; private set; }
    public List<MapStatRow> MapStats { get; private set; } = new();
    public List<MonthlyTrendRow> Trend { get; private set; } = new();
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var player = await _players.GetAsync(id);
        if (player is null)
        {
            return NotFound();
        }
        Player = player;

        To ??= Today;
        From ??= To.Value.AddYears(-1);
        if (From > To)
        {
            (From, To) = (To, From);
        }

        Totals = await _reports.PlayerTotalsAsync(id, From);
        MapStats = await _reports.MapStatsAsync(id, From.Value, To.Value);
        Trend = await _reports.MonthlyTrendAsync(id, From.Value, To.Value);
        return Page();
    }

    /// <summary>Monthly trend rows as JSON for the charts.</summary>
    public string TrendJson() => JsonSerializer.Serialize(new
    {
        labels = Trend.Select(t => t.Month.ToString("MMM yyyy", CultureInfo.InvariantCulture)),
        kd = Trend.Select(t => t.KillDeathRatio),
        adr = Trend.Select(t => t.AverageDamagePerRound),
        winRate = Trend.Select(t => t.WinRate),
        games = Trend.Select(t => t.NumGames)
    });

    public string RangeUrl(DateOnly from, DateOnly to) =>
        Url.Page("/Players/Profile", new { id = Player.PlayerId, From = Fmt.Iso(from), To = Fmt.Iso(to) }) ?? "#";
}
