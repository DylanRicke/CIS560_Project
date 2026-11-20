using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Compare;

/// <summary>
/// Compare a player with each accepted friend (aggregating query Report_FriendComparison),
/// send and answer friend requests (Friendships_Add / Friendships_Respond) and
/// unfriend or cancel (Friendships_Remove).
/// </summary>
public class IndexModel : PageModel
{
    private readonly PlayerRepository _players;
    private readonly FriendRepository _friends;
    private readonly ReportRepository _reports;

    public IndexModel(PlayerRepository players, FriendRepository friends, ReportRepository reports)
    {
        _players = players;
        _friends = friends;
        _reports = reports;
    }

    [BindProperty(SupportsGet = true)] public int? PlayerId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }

    public List<PlayerListItem> Players { get; private set; } = new();
    public PlayerListItem? Player { get; private set; }
    public List<FriendComparisonRow> Rows { get; private set; } = new();
    public List<FriendItem> Friends { get; private set; } = new();
    public List<FriendItem> Accepted => Friends.Where(f => f.IsAccepted).ToList();
    public List<FriendItem> Incoming => Friends.Where(f => f.IsIncomingRequest).ToList();
    public List<FriendItem> Outgoing => Friends.Where(f => f.IsOutgoingRequest).ToList();
    public List<PlayerListItem> Candidates { get; private set; } = new();

    /// <summary>The player's own numbers, which repeat on every comparison row.</summary>
    public FriendComparisonRow? Me => Rows.FirstOrDefault();

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        To ??= today;
        From ??= To.Value.AddYears(-1);
        if (From > To)
        {
            (From, To) = (To, From);
        }

        Players = await _players.SearchAsync();
        Player = Players.FirstOrDefault(p => p.PlayerId == PlayerId)
                 ?? Players.FirstOrDefault(p => p.PlayerId == 1)
                 ?? Players.FirstOrDefault();
        if (Player is null)
        {
            return;
        }
        PlayerId = Player.PlayerId;

        Rows = await _reports.FriendComparisonAsync(Player.PlayerId, From.Value, To.Value);
        Friends = await _friends.ListAsync(Player.PlayerId);

        // Anyone not already a friend or in a pending request can be asked (declined requests can be re-sent).
        var friendIds = Friends.Where(f => f.Status != "Declined").Select(f => f.OtherPlayerId).ToHashSet();
        Candidates = Players.Where(p => p.PlayerId != Player.PlayerId && !friendIds.Contains(p.PlayerId)).ToList();
    }

    public async Task<IActionResult> OnPostAddFriendAsync(int playerId, int? friendId)
    {
        if (friendId is null)
        {
            TempData["Message"] = "Choose a player to add.";
        }
        else
        {
            var status = "";
            var error = await DbCall.TryAsync(async () => status = await _friends.AddAsync(playerId, friendId.Value));
            TempData["Message"] = error ?? (status == "Accepted"
                ? "They had already sent a request, so you're now friends."
                : "Friend request sent. It shows as pending until they accept.");
        }
        return BackToPage(playerId);
    }

    public async Task<IActionResult> OnPostRespondAsync(int playerId, int requesterId, bool accept)
    {
        var error = await DbCall.TryAsync(() => _friends.RespondAsync(playerId, requesterId, accept));
        TempData["Message"] = error ?? (accept ? "Request accepted." : "Request declined.");
        return BackToPage(playerId);
    }

    public async Task<IActionResult> OnPostRemoveFriendAsync(int playerId, int friendId)
    {
        var error = await DbCall.TryAsync(() => _friends.RemoveAsync(playerId, friendId));
        TempData["Message"] = error ?? "Removed.";
        return BackToPage(playerId);
    }

    private IActionResult BackToPage(int playerId) =>
        RedirectToPage(new { PlayerId = playerId, From = Fmt.Iso(From), To = Fmt.Iso(To) });
}
