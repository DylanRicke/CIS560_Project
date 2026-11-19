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
            Input.Rank = "Unranked";
            return Page();
        }

        var player = await _players.GetAsync(id.Value);
        if (player is null)
        {
            return NotFound();
        }

        Input = new PlayerInput { Username = player.Username, Email = player.Email, Rank = player.Rank };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        PlayerId = id;
        if (!Ranks.All.Contains(Input.Rank))
        {
            ModelState.AddModelError("Input.Rank", "Choose a rank from the list.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var savedId = id ?? 0;
        var error = await DbCall.TryAsync(async () =>
        {
            if (id is null)
            {
                savedId = await _players.InsertAsync(Input.Username, Input.Email, Input.Rank);
            }
            else
            {
                await _players.UpdateAsync(id.Value, Input.Username, Input.Email, Input.Rank);
            }
        });

        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return Page();
        }

        TempData["Message"] = id is null ? $"{Input.Username} added." : "Player saved.";
        return RedirectToPage("/Players/Profile", new { id = savedId });
    }
}

public class PlayerInput
{
    [Required(ErrorMessage = "Enter a username.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Usernames are 3 to 32 characters.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Enter an email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(255)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Choose a rank.")]
    public string Rank { get; set; } = "Unranked";
}
