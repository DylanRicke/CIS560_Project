using System.Globalization;
using System.Text.Json;
using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Players;

/// <summary>
/// A player's page. Uses three of the aggregating queries
/// (Report_PlayerTotals, Report_PlayerMapStats, Report_PlayerMonthlyTrend),
/// the player's rank history, and remove / restore (Players_SetRemoved).
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
    public List<PremierPoint> PremierHistory { get; private set; } = new();
    public List<CompetitiveMapRank> CompetitiveByMap { get; private set; } = new();
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
        PremierHistory = await _players.PremierHistoryAsync(id, From.Value, To.Value);
        CompetitiveByMap = await _players.CompetitiveByMapAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostSetRemovedAsync(int id, bool remove)
    {
        var error = await DbCall.TryAsync(() => _players.SetRemovedAsync(id, remove));
        TempData["Message"] = error ?? (remove
            ? "Player removed. They're hidden from lists and friend comparisons; their past matches stay on the scoreboards."
            : "Player restored.");
        return RedirectToPage(new { id });
    }

    /// <summary>Premier rating points as JSON for the rating chart.</summary>
    public string PremierJson() => JsonSerializer.Serialize(new
    {
        labels = PremierHistory.Select(p => p.RecordedOn.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)),
        rating = PremierHistory.Select(p => p.RankValue)
    });

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
