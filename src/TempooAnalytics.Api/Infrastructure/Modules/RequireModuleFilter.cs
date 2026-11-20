using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TempooAnalytics.Api.Infrastructure.Modules;

/// <summary>
/// Razor pages are discovered by the framework, not mapped by a module. This resource filter runs before the page
/// model is created, so a page that needs services of a module answers 404 (instead of failing to activate) when
/// that module is not loaded.
/// </summary>
public sealed class RequireModuleFilter(Type moduleType) : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var catalog = context.HttpContext.RequestServices.GetService<FeatureModuleCatalog>();
        if (catalog?.Modules.Any(m => moduleType.IsInstanceOfType(m)) != true) context.Result = new NotFoundResult();
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
