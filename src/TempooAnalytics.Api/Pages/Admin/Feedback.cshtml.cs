using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Feedback.Models;

namespace TempooAnalytics.Api.Pages.Admin;

public class FeedbackModel(AnalyticsDbContext db, IConfiguration config) : PageModel
{
    public List<Features.Feedback.Models.Feedback> AllFeedback { get; set; } = new();

    [BindProperty]
    public string? MasterPassword { get; set; }

    public bool IsUnlocked { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        CheckSession();
        if (IsUnlocked)
        {
            AllFeedback = await db.Feedbacks
                .Include(f => f.User)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();
        }
    }

    public IActionResult OnPostUnlock()
    {
        var master = config["Management:MasterPassword"];
        var apiKey = config["Management:ApiKey"];
        
        if ((!string.IsNullOrEmpty(master) && MasterPassword == master) || 
            (!string.IsNullOrEmpty(apiKey) && MasterPassword == apiKey)) 
        { 
            HttpContext.Session.SetString("AdminUnlocked", "true"); 
            return RedirectToPage(); 
        }
        
        ErrorMessage = "Invalid Master Password";
        return Page();
    }

    public IActionResult OnPostLogout() 
    { 
        HttpContext.Session.Remove("AdminUnlocked"); 
        return RedirectToPage(); 
    }

    private void CheckSession() => IsUnlocked = HttpContext.Session.GetString("AdminUnlocked") == "true";
}
