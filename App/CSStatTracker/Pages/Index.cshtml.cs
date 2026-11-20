using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSStatTracker.Pages;

/// <summary>The site opens on the match list.</summary>
public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Matches/Index");
}
