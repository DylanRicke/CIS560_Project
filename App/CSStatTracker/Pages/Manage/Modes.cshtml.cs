using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Manage;

/// <summary>List, add and rename game modes (GameModes_List, GameModes_Insert, GameModes_Update).</summary>
public class ModesModel : PageModel
{
    private readonly LookupRepository _lookups;

    public ModesModel(LookupRepository lookups) => _lookups = lookups;

    public List<GameModeItem> Modes { get; private set; } = new();
    public string? Error { get; private set; }

    public async Task OnGetAsync() => Modes = await _lookups.ModesAsync();

    public async Task<IActionResult> OnPostAddAsync(string? modeName)
    {
        var problem = Validate(modeName);
        if (problem is not null) return await ShowError(problem);

        var error = await DbCall.TryAsync(() => _lookups.InsertModeAsync(modeName!));
        if (error is not null) return await ShowError(error);

        TempData["Message"] = $"{modeName!.Trim()} added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int gameModeId, string? modeName)
    {
        var problem = Validate(modeName);
        if (problem is not null) return await ShowError(problem);

        var error = await DbCall.TryAsync(() => _lookups.UpdateModeAsync(gameModeId, modeName!));
        if (error is not null) return await ShowError(error);

        TempData["Message"] = $"{modeName!.Trim()} saved.";
        return RedirectToPage();
    }

    private static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a mode name.";
        if (name.Trim().Length > 32) return "Mode names are at most 32 characters.";
        return null;
    }

    private async Task<IActionResult> ShowError(string message)
    {
        Error = message;
        Modes = await _lookups.ModesAsync();
        return Page();
    }
}
