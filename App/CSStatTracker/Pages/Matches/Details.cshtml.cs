using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Matches;

/// <summary>One match's scoreboard and round timeline, plus delete (Matches_Delete).</summary>
public class DetailsModel : PageModel
{
    private readonly MatchRepository _matches;

    public DetailsModel(MatchRepository matches) => _matches = matches;

    public MatchDetail Match { get; private set; } = null!;
    public List<ScoreboardRow> Rows { get; private set; } = new();
    public List<RoundRow> Rounds { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var match = await _matches.GetAsync(id);
        if (match is null)
        {
            return NotFound();
        }

        Match = match;
        Rows = await _matches.ScoreboardAsync(id);
        Rounds = await _matches.RoundsAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, bool confirm)
    {
        if (!confirm)
        {
            TempData["Message"] = "Tick the box to confirm before deleting a match.";
            return RedirectToPage(new { id });
        }

        var error = await DbCall.TryAsync(() => _matches.DeleteAsync(id));
        if (error is not null)
        {
            TempData["Message"] = error;
            return RedirectToPage(new { id });
        }

        TempData["Message"] = $"Match #{id} deleted, with its stat lines and rounds.";
        return RedirectToPage("/Matches/Index");
    }

    /// <summary>Groups the scoreboard by the stored TeamNumber.</summary>
    public IEnumerable<(string Title, string? Outcome, List<ScoreboardRow> Players)> Teams()
    {
        foreach (var team in new[] { 1, 2 })
        {
            var players = Rows.Where(r => r.TeamNumber == team).ToList();
            var mine = team == 1 ? Match.Team1Score : Match.Team2Score;
            var theirs = team == 1 ? Match.Team2Score : Match.Team1Score;
            var outcome = mine > theirs ? "Won" : mine < theirs ? "Lost" : "Tie";
            yield return ($"Team {team} · {mine}", outcome, players);
        }
    }
}
