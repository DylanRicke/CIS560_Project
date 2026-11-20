using CSStatTracker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages.Manage;

/// <summary>List, add and edit maps (Maps_List, Maps_Insert, Maps_Update).</summary>
public class MapsModel : PageModel
{
    private readonly LookupRepository _lookups;

    public MapsModel(LookupRepository lookups) => _lookups = lookups;

    public List<MapItem> Maps { get; private set; } = new();
    public string? Error { get; private set; }

    public async Task OnGetAsync() => Maps = await _lookups.MapsAsync();

    public async Task<IActionResult> OnPostAddAsync(string? mapName, string? mapType)
    {
        if (string.IsNullOrWhiteSpace(mapName) || !MapTypes.All.Contains(mapType))
        {
            return await ShowError("Enter a map name and choose a type.");
        }

        var error = await DbCall.TryAsync(() => _lookups.InsertMapAsync(mapName, mapType!));
        if (error is not null) return await ShowError(error);

        TempData["Message"] = $"{mapName.Trim()} added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int mapId, string? mapName, string? mapType)
    {
        if (string.IsNullOrWhiteSpace(mapName) || !MapTypes.All.Contains(mapType))
        {
            return await ShowError("A map needs a name and a type.");
        }

        var error = await DbCall.TryAsync(() => _lookups.UpdateMapAsync(mapId, mapName, mapType!));
        if (error is not null) return await ShowError(error);

        TempData["Message"] = $"{mapName.Trim()} saved.";
        return RedirectToPage();
    }

    private async Task<IActionResult> ShowError(string message)
    {
        Error = message;
        Maps = await _lookups.MapsAsync();
        return Page();
    }
}
