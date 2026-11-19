using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Players;

/// <summary>Search and list players (Players_Search).</summary>
public class IndexModel : PageModel
{
    private readonly PlayerRepository _players;

    public IndexModel(PlayerRepository players) => _players = players;

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Rank { get; set; }

    public List<PlayerListItem> Results { get; private set; } = new();

    public async Task OnGetAsync() => Results = await _players.SearchAsync(Search, Rank);
}
