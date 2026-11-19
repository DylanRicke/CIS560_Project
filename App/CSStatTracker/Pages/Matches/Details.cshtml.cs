using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Matches;

/// <summary>One match's scoreboard, plus remove / restore (soft delete).</summary>
public class DetailsModel : PageModel
{
    private readonly MatchRepository _matches;

    public DetailsModel(MatchRepository matches) => _matches = matches;

    public MatchDetail Match { get; private set; } = null!;
    public List<ScoreboardRow> Rows { get; private set; } = new();
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var match = await _matches.GetAsync(id);
        if (match is null)
        {
            return NotFound();
        }

        Match = match;
        Rows = await _matches.ScoreboardAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostSetRemovedAsync(int id, bool remove)
    {
        var error = await DbCall.TryAsync(() => _matches.SetRemovedAsync(id, remove));
        TempData["Message"] = error ?? (remove
            ? "Match removed. It no longer counts toward anyone's stats. You can restore it here."
            : "Match restored.");
        return RedirectToPage(new { id });
    }

    /// <summary>Groups the scoreboard by side. A tie has no stored sides, so it is one group.</summary>
    public IEnumerable<(string Title, string? Outcome, List<ScoreboardRow> Players)> Teams()
    {
        if (Match.IsTie)
        {
            yield return ("Both teams", "Tie · sides aren't stored for a tied match", Rows);
            yield break;
        }

        foreach (var team in new[] { 1, 2 })
        {
            var players = Rows.Where(r => r.Team == team).ToList();
            var score = team == 1 ? Match.Team1Score : Match.Team2Score;
            var won = team == 1 ? Match.Team1Score > Match.Team2Score : Match.Team2Score > Match.Team1Score;
            yield return ($"Team {team} · {score}", won ? "Won" : "Lost", players);
        }
    }
}
