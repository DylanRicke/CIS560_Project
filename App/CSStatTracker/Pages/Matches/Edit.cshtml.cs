using System.ComponentModel.DataAnnotations;
using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Matches;

/// <summary>Fix a recorded match's map, date and stat lines (Tracker.Matches_Update).</summary>
public class EditModel : PageModel
{
    private readonly MatchRepository _matches;
    private readonly LookupRepository _lookups;

    public EditModel(MatchRepository matches, LookupRepository lookups)
    {
        _matches = matches;
        _lookups = lookups;
    }

    [BindProperty] public EditInput Input { get; set; } = new();

    public MatchDetail Match { get; private set; } = null!;
    public List<MapItem> Maps { get; private set; } = new();
    public Dictionary<int, ScoreboardRow> Players { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await LoadAsync(id) is { } redirect)
        {
            return redirect;
        }

        Input = new EditInput
        {
            MapId = Match.MapId,
            MatchDate = Match.MatchDate,
            Rows = Players.Values.Select(r => new EditRow
            {
                PlayerId = r.PlayerId,
                Kills = r.Kills,
                Deaths = r.Deaths,
                Assists = r.Assists,
                HeadshotPercentage = r.HeadshotPercentage,
                Damage = r.Damage
            }).ToList()
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await LoadAsync(id) is { } redirect)
        {
            return redirect;
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var stats = Input.Rows.Select(r => new PlayerStatInput
        {
            PlayerId = r.PlayerId,
            TeamNumber = Players.TryGetValue(r.PlayerId, out var row) ? row.TeamNumber : 1,   // ignored by Matches_Update
            Kills = r.Kills,
            Deaths = r.Deaths,
            Assists = r.Assists,
            HeadshotPercentage = r.HeadshotPercentage,
            Damage = r.Damage
        });

        var error = await DbCall.TryAsync(() =>
            _matches.UpdateAsync(id, Input.MapId!.Value, Input.MatchDate!.Value, stats));

        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return Page();
        }

        TempData["Message"] = "Match updated.";
        return RedirectToPage("/Matches/Details", new { id });
    }

    private async Task<IActionResult?> LoadAsync(int id)
    {
        var match = await _matches.GetAsync(id);
        if (match is null)
        {
            return NotFound();
        }
        Match = match;
        Maps = await _lookups.MapsAsync();
        Players = (await _matches.ScoreboardAsync(id)).ToDictionary(r => r.PlayerId);
        return null;
    }

    public string TeamLabel(int playerId) =>
        Players.TryGetValue(playerId, out var row) ? $"Team {row.TeamNumber}" : "—";
}

public class EditInput
{
    [Required(ErrorMessage = "Choose a map.")]
    public int? MapId { get; set; }

    [Required(ErrorMessage = "Enter when the match was played.")]
    public DateTime? MatchDate { get; set; }

    public List<EditRow> Rows { get; set; } = new();
}

public class EditRow
{
    public int PlayerId { get; set; }

    [Range(0, 200, ErrorMessage = "Kills must be between 0 and 200.")]
    public int Kills { get; set; }

    [Range(0, 200, ErrorMessage = "Deaths must be between 0 and 200.")]
    public int Deaths { get; set; }

    [Range(0, 200, ErrorMessage = "Assists must be between 0 and 200.")]
    public int Assists { get; set; }

    [Range(0, 100, ErrorMessage = "Headshot % must be between 0 and 100.")]
    public decimal HeadshotPercentage { get; set; }

    [Range(0, 50000, ErrorMessage = "Damage must be between 0 and 50,000.")]
    public int Damage { get; set; }
}
