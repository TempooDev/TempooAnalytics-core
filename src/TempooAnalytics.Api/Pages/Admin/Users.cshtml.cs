using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Admin;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Email;

namespace TempooAnalytics.Api.Pages.Admin;

public class UserAdminDto
{
    public User User { get; set; } = null!;
    public string Plan { get; set; } = "free";

    /// <summary>What the modules contribute (<see cref="IUserAdminContributor"/>) per contributor key.</summary>
    public Dictionary<string, string> Extras { get; set; } = new();
}

public class UsersModel(IUserRepository users, IUserPlanStore plans, AnalyticsDbContext db, IConfiguration config, IEmailService emailService, HybridCache cache, IEnumerable<IUserAdminContributor> contributors) : PageModel
{
    /// <summary>Columns and panels the loaded modules add (the AI credits, in the cloud). None in community.</summary>
    public IReadOnlyList<IUserAdminContributor> Contributors { get; } = contributors.OrderBy(c => c.Key, StringComparer.Ordinal).ToList();

    public List<UserAdminDto> AllUsers { get; set; } = new();
    /// <summary>Plans an admin can assign (none in community).</summary>
    public IReadOnlyList<PlanOption> AllTiers { get; set; } = [];

    [BindProperty]
    public string? MasterPassword { get; set; }

    public bool IsUnlocked { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    [BindProperty]
    public string NewUsername { get; set; } = "";
    [BindProperty]
    public string NewPassword { get; set; } = "";
    [BindProperty]
    public string? NewEmail { get; set; }
    [BindProperty]
    public bool NewIsAdmin { get; set; }
    [BindProperty]
    public string NewTier { get; set; } = "free";

    // Edit User Properties
    [BindProperty]
    public Guid? EditUserId { get; set; }
    [BindProperty]
    public string EditUsername { get; set; } = "";
    [BindProperty]
    public string? EditEmail { get; set; }
    [BindProperty]
    public bool EditIsAdmin { get; set; }
    [BindProperty]
    public bool EditIsActive { get; set; }
    [BindProperty]
    public string? EditPassword { get; set; }
    [BindProperty]
    public string EditTier { get; set; } = "free";


    private const string ContributionMessageKey = "ContributionMessage";

    internal const string NoEmailProviderMessage = "Usuario creado. No hay proveedor de email configurado (Resend), así que no se ha enviado ningún correo de bienvenida: comunica la contraseña al usuario por otro medio.";

    public async Task OnGetAsync()
    {
        CheckSession();
        SuccessMessage = TempData[ContributionMessageKey] as string;
        if (IsUnlocked) await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var rawUsers = await users.GetAllUsersAsync();
        AllTiers = await plans.ListPlansAsync();
        
        var columns = new Dictionary<string, IReadOnlyDictionary<UserId, string>>();
        foreach (var c in Contributors) columns[c.Key] = await c.LoadAsync(HttpContext.RequestAborted);

        AllUsers = new List<UserAdminDto>();
        foreach (var u in rawUsers)
        {
            var plan = await plans.GetSubscribedPlanAsync(u.Id);
            AllUsers.Add(new UserAdminDto
            {
                User = u,
                Plan = plan ?? "free",
                Extras = Contributors.ToDictionary(c => c.Key, c => columns[c.Key].GetValueOrDefault(u.Id, c.DefaultValue))
            });
        }
    }

    public async Task<IActionResult> OnPostUnlockAsync()
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

    public async Task<IActionResult> OnPostCreateUserAsync()
    {
        CheckSession();
        if (!IsUnlocked) return Page();
        
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewPassword)) 
        { 
            ErrorMessage = "Required fields missing"; 
            await LoadDataAsync();
            return Page(); 
        }

        if (await users.IsUsernameTakenAsync(NewUsername)) 
        {
            ErrorMessage = "El nombre de usuario ya está en uso";
        }
        else if (!string.IsNullOrWhiteSpace(NewEmail) && await users.IsEmailTakenAsync(NewEmail))
        {
            ErrorMessage = "El email ya está registrado con otro usuario";
        }
        else 
        { 
            var normalizedUsername = NewUsername.ToLowerInvariant().Trim();
            var normalizedEmail = NewEmail?.ToLowerInvariant().Trim();

            var newUser = new User { Id = UserId.New(), Username = normalizedUsername, Email = normalizedEmail, IsAdmin = NewIsAdmin, IsActive = true };
            await users.CreateUserAsync(newUser, NewPassword); 
            
            if (NewTier != "free")
            {
                await plans.SetPlanAsync(newUser.Id, NewTier, "{\"is_manual\": true}");
                await cache.RemoveAsync($"sub_info_{newUser.Id.Value}");
            }

            // Without an email provider (self-hosted without Resend) nothing is sent: say so instead of claiming a welcome email.
            var emailAvailable = emailService.IsConfigured;
            if (emailAvailable && !string.IsNullOrWhiteSpace(normalizedEmail))
            {
                try
                {
                    await emailService.SendWelcomeWithCredentialsEmailAsync(normalizedEmail, normalizedUsername, NewPassword);
                }
                catch { /* Ignore send failures in the UI, the user was already created */ }
            }
            
            SuccessMessage = emailAvailable ? "User created and welcome email sent" : NoEmailProviderMessage;
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateUserAsync()
    {
        CheckSession();
        if (!IsUnlocked || !EditUserId.HasValue) return Page();

        var userId = new UserId(EditUserId.Value);
        var user = await users.FindByIdAsync(userId);
        if (user == null)
        {
            ErrorMessage = "User not found";
        }
        else
        {
            if (await users.IsUsernameTakenAsync(EditUsername, userId))
            {
                ErrorMessage = "El nuevo nombre de usuario ya está en uso";
            }
            else if (!string.IsNullOrWhiteSpace(EditEmail) && await users.IsEmailTakenAsync(EditEmail, userId))
            {
                ErrorMessage = "El nuevo email ya está registrado con otro usuario";
            }
            else
            {
                user.Username = EditUsername;
                user.Email = EditEmail;
                user.IsAdmin = EditIsAdmin;
                user.IsActive = EditIsActive;
                await users.UpdateUserAsync(user);

                // Update Tier
                var currentPlan = await plans.GetSubscribedPlanAsync(userId);
                if ((currentPlan ?? "free") != EditTier)
                {
                    await plans.SetPlanAsync(userId, EditTier, "{\"is_manual\": true}");
                    await cache.RemoveAsync($"sub_info_{userId.Value}");
                }

                // If a manual password was provided, update it
                if (!string.IsNullOrWhiteSpace(EditPassword))
                {
                    await users.ForceResetPasswordAsync(user.Id, EditPassword);
                    SuccessMessage = "Usuario y contraseña actualizados correctamente";
                }
                else
                {
                    SuccessMessage = "Usuario actualizado correctamente";
                }
            }
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostContributionAsync(string key)
    {
        CheckSession();
        if (!IsUnlocked) return Page();
        var contributor = Contributors.FirstOrDefault(c => c.Key == key);
        if (contributor == null) return NotFound();

        var result = await contributor.ApplyAsync(Request.Form, HttpContext.RequestAborted);
        if (result.Error == null)
        {
            // Post/Redirect/Get: reloading the page must not apply the change again.
            TempData[ContributionMessageKey] = result.Success;
            return RedirectToPage();
        }

        ErrorMessage = result.Error;
        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostClearCacheAsync(Guid userId)
    {
        CheckSession();
        if (!IsUnlocked) return Page();

        await cache.RemoveAsync($"sub_info_{userId}");
        SuccessMessage = "Caché de suscripción invalidada correctamente";
        
        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(Guid userId)
    {
        CheckSession();
        if (!IsUnlocked) return Page();

        var user = await users.FindByIdAsync(new UserId(userId));
        if (user == null)
        {
            ErrorMessage = "User not found";
        }
        else
        {
            var newPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)); // 8 chars
            await users.ForceResetPasswordAsync(user.Id, newPassword);
            
            if (!string.IsNullOrWhiteSpace(user.Email) && emailService.IsConfigured)
            {
                try
                {
                    await emailService.SendWelcomeWithCredentialsEmailAsync(user.Email, user.Username, newPassword);
                    SuccessMessage = $"Password reset for {user.Username} and sent to {user.Email}";
                }
                catch
                {
                    SuccessMessage = $"Password reset for {user.Username} to: {newPassword} (Email failed)";
                }
            }
            else
            {
                SuccessMessage = emailService.IsConfigured
                    ? $"Password reset for {user.Username} to: {newPassword}"
                    : $"Contraseña de {user.Username} restablecida a: {newPassword}. No hay proveedor de email configurado, así que no se ha enviado ningún correo: comunícasela al usuario por otro medio.";
            }
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(Guid userId)
    {
        CheckSession();
        if (!IsUnlocked) return Page();

        await users.DeleteUserAsync(new UserId(userId));
        SuccessMessage = "Usuario eliminado correctamente";
        
        await LoadDataAsync();
        return Page();
    }

    public IActionResult OnPostLogout() { HttpContext.Session.Remove("AdminUnlocked"); return RedirectToPage(); }
    private void CheckSession() => IsUnlocked = HttpContext.Session.GetString("AdminUnlocked") == "true";
}
