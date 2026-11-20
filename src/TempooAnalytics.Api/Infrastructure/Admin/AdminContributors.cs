using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Infrastructure.Admin;

/// <summary>
/// A link of the admin console navigation that a module contributes with the page it owns. The core pages link a module's page only when
/// the module that serves it is loaded (a page of an absent module answers 404), so they ask <see cref="AdminNav.Has"/> instead of naming the module.
/// </summary>
public interface IAdminNavContributor
{
    /// <summary>The page the module serves (for example <c>/admin/ai-usage</c>).</summary>
    string Href { get; }

    /// <summary>Text of the link in the navigation (for example <c>Leads</c>).</summary>
    string Label { get; }

    /// <summary>Position in the navigation: links below <see cref="AdminNav.FeedbackOrder"/> go before the core's Feedback link, the rest after it.</summary>
    int Order { get; }
}

public static class AdminNav
{
    /// <summary>Position of the core's own Feedback link among the contributed ones.</summary>
    public const int FeedbackOrder = 50;

    /// <summary>The contributed links to show before (<paramref name="beforeFeedback"/>) or after the core's Feedback link, in order.</summary>
    public static IEnumerable<IAdminNavContributor> Links(HttpContext http, bool beforeFeedback) =>
        http.RequestServices.GetServices<IAdminNavContributor>()
            .Where(c => beforeFeedback ? c.Order < FeedbackOrder : c.Order >= FeedbackOrder)
            .OrderBy(c => c.Order);

    /// <summary>True when a loaded module contributes <paramref name="href"/>.</summary>
    public static bool Has(HttpContext http, string href) =>
        http.RequestServices.GetServices<IAdminNavContributor>().Any(c => string.Equals(c.Href, href, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Result of a contributor handling the form of its panel in <c>Admin/Users</c>.</summary>
public sealed record UserAdminActionResult(string? Success = null, string? Error = null);

/// <summary>
/// What a module adds to <c>Pages/Admin/Users</c>: one column in the user table (a value per user) and one panel next to the edit form,
/// with the form that module owns. The page renders and routes them without knowing what they are (the AI module contributes the credits).
/// Without contributors (community) the page shows neither.
/// </summary>
public interface IUserAdminContributor
{
    /// <summary>Identifies the contributor in the page's form post; unique and URL-safe.</summary>
    string Key { get; }

    /// <summary>Header of the column.</summary>
    string ColumnHeader { get; }

    /// <summary>What the column and the panel show for a user the contributor has nothing about.</summary>
    string DefaultValue { get; }

    /// <summary>Absolute path of the Razor partial rendered as the panel (for example <c>/Pages/Admin/_UsersAiCredits.cshtml</c>). Its form posts to <see cref="UserAdminForm.PostHandler"/>.</summary>
    string PanelPartial { get; }

    /// <summary>Value shown for each user that has one.</summary>
    Task<IReadOnlyDictionary<UserId, string>> LoadAsync(CancellationToken ct);

    /// <summary>Applies the panel's form (the fields of the partial). A success redirects back to the page with the message; an error is shown on it.</summary>
    Task<UserAdminActionResult> ApplyAsync(IFormCollection form, CancellationToken ct);
}

public static class UserAdminForm
{
    /// <summary>The page handler the panel forms post to: <c>?handler=Contribution&amp;key=&lt;Key&gt;</c>.</summary>
    public const string PostHandler = "Contribution";
}
