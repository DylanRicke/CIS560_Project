using System.ComponentModel.DataAnnotations;
using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CSStatTracker.Pages.Matches;

/// <summary>
/// Manual data import: one match plus a stat line per player, saved by
/// Tracker.Matches_Import in a single transaction.
/// </summary>
public class ImportModel : PageModel
{
    /// <summary>Rows drawn per team. The script hides the ones past the mode's team size.</summary>
    public const int MaxRowsPerTeam = 10;

    private readonly MatchRepository _matches;
    private readonly LookupRepository _lookups;
    private readonly PlayerRepository _players;

    public ImportModel(MatchRepository matches, LookupRepository lookups, PlayerRepository players)
    {
        _matches = matches;
        _lookups = lookups;
        _players = players;
    }

    [BindProperty] public ImportInput Input { get; set; } = new();

    public List<MapItem> Maps { get; private set; } = new();
    public List<GameModeItem> Modes { get; private set; } = new();
    public List<SelectListItem> PlayerOptions { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await LoadListsAsync();
        var now = DateTime.Now;
        Input.MatchDate = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
        Input.GameModeId = (Modes.FirstOrDefault(m => m.ModeName == "Competitive") ?? Modes.FirstOrDefault())?.GameModeId;
        PadRows();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadListsAsync();

        var stats = new List<PlayerStatInput>();
        AddTeam(stats, Input.Team1, 1);
        AddTeam(stats, Input.Team2, 2);

        if (stats.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one player.");
        }

        var mode = Modes.FirstOrDefault(m => m.GameModeId == Input.GameModeId);
        if (mode is not null)
        {
            foreach (var team in stats.GroupBy(s => s.TeamNumber).Where(g => g.Count() > mode.PlayersPerTeam))
            {
                ModelState.AddModelError(string.Empty,
                    $"{mode.ModeName} is {mode.PlayersPerTeam}v{mode.PlayersPerTeam}, but Team {team.Key} has {team.Count()} players.");
            }
        }

        foreach (var dup in stats.GroupBy(s => s.PlayerId).Where(g => g.Count() > 1))
        {
            var name = PlayerOptions.FirstOrDefault(o => o.Value == dup.Key.ToString())?.Text ?? $"Player {dup.Key}";
            ModelState.AddModelError(string.Empty, $"{name} is listed more than once.");
        }

        if (!ModelState.IsValid)
        {
            PadRows();
            return Page();
        }

        var newId = 0;
        var error = await DbCall.TryAsync(async () =>
            newId = await _matches.ImportAsync(Input.MapId!.Value, Input.GameModeId!.Value, Input.MatchDate!.Value,
                                               Input.Team1Score!.Value, Input.Team2Score!.Value, stats));

        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            PadRows();
            return Page();
        }

        TempData["Message"] = $"Match imported with {stats.Count} players.";
        return RedirectToPage("/Matches/Details", new { id = newId });
    }

    /// <summary>Rows with a player chosen become stat lines; blank numbers count as 0.</summary>
    private void AddTeam(List<PlayerStatInput> stats, List<StatRow> rows, int team)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.PlayerId is null)
            {
                if (row.HasNumbers)
                {
                    ModelState.AddModelError(string.Empty, $"Team {team}, row {i + 1}: choose a player or clear the numbers.");
                }
                continue;
            }

            stats.Add(new PlayerStatInput
            {
                PlayerId = row.PlayerId.Value,
                TeamNumber = team,
                Kills = row.Kills ?? 0,
                Deaths = row.Deaths ?? 0,
                Assists = row.Assists ?? 0,
                HeadshotPercentage = row.HeadshotPercentage ?? 0,
                Damage = row.Damage ?? 0
            });
        }
    }

    private void PadRows()
    {
        while (Input.Team1.Count < MaxRowsPerTeam) Input.Team1.Add(new StatRow());
        while (Input.Team2.Count < MaxRowsPerTeam) Input.Team2.Add(new StatRow());
    }

    private async Task LoadListsAsync()
    {
        Maps = await _lookups.MapsAsync();
        Modes = await _lookups.ModesAsync();
        PlayerOptions = (await _players.SearchAsync())
            .Select(p => new SelectListItem(p.DisplayName, p.PlayerId.ToString()))
            .ToList();
    }
}

public class ImportInput
{
    [Required(ErrorMessage = "Choose a map.")]
    public int? MapId { get; set; }

    [Required(ErrorMessage = "Choose a game mode.")]
    public int? GameModeId { get; set; }

    [Required(ErrorMessage = "Enter when the match was played.")]
    public DateTime? MatchDate { get; set; }

    [Required(ErrorMessage = "Enter Team 1's score.")]
    [Range(0, 60, ErrorMessage = "Team 1's score must be between 0 and 60.")]
    public int? Team1Score { get; set; }

    [Required(ErrorMessage = "Enter Team 2's score.")]
    [Range(0, 60, ErrorMessage = "Team 2's score must be between 0 and 60.")]
    public int? Team2Score { get; set; }

    public List<StatRow> Team1 { get; set; } = new();
    public List<StatRow> Team2 { get; set; } = new();
}

public class StatRow
{
    public int? PlayerId { get; set; }

    [Range(0, 200, ErrorMessage = "Kills must be between 0 and 200.")]
    public int? Kills { get; set; }

    [Range(0, 200, ErrorMessage = "Deaths must be between 0 and 200.")]
    public int? Deaths { get; set; }

    [Range(0, 200, ErrorMessage = "Assists must be between 0 and 200.")]
    public int? Assists { get; set; }

    [Range(0, 100, ErrorMessage = "Headshot % must be between 0 and 100.")]
    public decimal? HeadshotPercentage { get; set; }

    [Range(0, 50000, ErrorMessage = "Damage must be between 0 and 50,000.")]
    public int? Damage { get; set; }

    public bool HasNumbers =>
        (Kills ?? 0) + (Deaths ?? 0) + (Assists ?? 0) + (HeadshotPercentage ?? 0) + (Damage ?? 0) > 0;
}
