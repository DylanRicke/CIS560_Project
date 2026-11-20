using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Matches;

/// <summary>Search and list matches (Matches_Search).</summary>
public class IndexModel : PageModel
{
    public const int PageSize = 25;

    private readonly MatchRepository _matches;
    private readonly LookupRepository _lookups;
    private readonly PlayerRepository _players;

    public IndexModel(MatchRepository matches, LookupRepository lookups, PlayerRepository players)
    {
        _matches = matches;
        _lookups = lookups;
        _players = players;
    }

    [BindProperty(SupportsGet = true)] public int? PlayerId { get; set; }
    [BindProperty(SupportsGet = true)] public int? MapId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ModeId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public List<MatchListItem> Results { get; private set; } = new();
    public List<MapItem> Maps { get; private set; } = new();
    public List<GameModeItem> Modes { get; private set; } = new();
    public List<PlayerListItem> Players { get; private set; } = new();
    public int TotalRows { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)PageSize));
    public string? SelectedPlayerName => Players.FirstOrDefault(p => p.PlayerId == PlayerId)?.DisplayName;

    public async Task OnGetAsync()
    {
        if (PageNumber < 1) PageNumber = 1;

        Maps = await _lookups.MapsAsync();
        Modes = await _lookups.ModesAsync();
        Players = await _players.SearchAsync(includeRemoved: true);
        Results = await _matches.SearchAsync(new MatchSearch(
            PlayerId, MapId, ModeId, From, To, PageNumber, PageSize));
        TotalRows = Results.FirstOrDefault()?.TotalRows ?? 0;
    }

    public string? PageUrl(int pageNumber) => Url.Page("/Matches/Index", new
    {
        PlayerId,
        MapId,
        ModeId,
        From = Fmt.Iso(From),
        To = Fmt.Iso(To),
        PageNumber = pageNumber
    });
}
