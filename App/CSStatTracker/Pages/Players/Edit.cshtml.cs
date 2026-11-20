using System.ComponentModel.DataAnnotations;
using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Players;

/// <summary>Add a player (Players_Insert) or edit one (Players_Update).</summary>
public class EditModel : PageModel
{
    private readonly PlayerRepository _players;

    public EditModel(PlayerRepository players) => _players = players;

    [BindProperty] public PlayerInput Input { get; set; } = new();

    public int? PlayerId { get; private set; }
    public bool IsNew => PlayerId is null;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        PlayerId = id;
        if (id is null)
        {
            return Page();
        }

        var player = await _players.GetAsync(id.Value);
        if (player is null)
        {
            return NotFound();
        }

        Input = new PlayerInput
        {
            SteamId = player.SteamId,
            DisplayName = player.DisplayName,
            PremierRank = player.PremierRank,
            CompetitiveRank = player.CompetitiveRank
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        PlayerId = id;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var savedId = id ?? 0;
        var error = await DbCall.TryAsync(async () =>
        {
            if (id is null)
            {
                savedId = await _players.InsertAsync(Input.SteamId, Input.DisplayName, Input.PremierRank, Input.CompetitiveRank);
            }
            else
            {
                await _players.UpdateAsync(id.Value, Input.SteamId, Input.DisplayName, Input.PremierRank, Input.CompetitiveRank);
            }
        });

        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return Page();
        }

        TempData["Message"] = id is null ? $"{Input.DisplayName.Trim()} added." : "Player saved.";
        return RedirectToPage("/Players/Profile", new { id = savedId });
    }
}

public class PlayerInput
{
    [Required(ErrorMessage = "Enter the player's SteamID.")]
    [RegularExpression(@"^\s*\d{17}\s*$", ErrorMessage = "A SteamID is exactly 17 digits (SteamID64).")]
    public string SteamId { get; set; } = "";

    [Required(ErrorMessage = "Enter a display name.")]
    [StringLength(32, ErrorMessage = "Display names are at most 32 characters.")]
    public string DisplayName { get; set; } = "";

    [Range(0, 40000, ErrorMessage = "Premier rating must be between 0 and 40,000.")]
    public int? PremierRank { get; set; }

    [Range(1, 18, ErrorMessage = "Choose a Competitive rank from the list.")]
    public int? CompetitiveRank { get; set; }
}
