using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TempooAnalytics.Api.Features.Imports.Endpoints;
using TempooAnalytics.Api.Features.Imports.Services;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using TempooAnalytics.Api.Infrastructure.Services;

namespace TempooAnalytics.Api.Features.Imports.Ga4;

public record Ga4AuthorizeResponse(string AuthorizationUrl);
public record Ga4ConnectRequest(string? Code, string? State);
public record Ga4PropertyDto(string Id, string Name, string Account);
public record Ga4ConnectResponse(string ConnectionId, IReadOnlyList<Ga4PropertyDto> Properties);
public record Ga4ImportRequest(string? ConnectionId, string? PropertyId);

/// <summary>
/// Google Analytics 4 import: OAuth authorization (code flow with PKCE, read-only, no offline access),
/// property choice and job creation. Google codes, states and tokens are never logged or returned.
/// </summary>
public static class Ga4ImportEndpoints
{
    public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string Scope = "https://www.googleapis.com/auth/analytics.readonly";

    private const string ExpiredMessage = "The Google authorization expired or is not valid. Connect with Google again.";

    public static void MapGa4ImportEndpoints(this RouteGroupBuilder imports)
    {
        var group = imports.MapGroup("/ga4")
            // Not released yet: behave as if the endpoints didn't exist.
            .AddEndpointFilter(async (context, next) =>
            {
                var services = context.HttpContext.RequestServices;
                if (!services.GetRequiredService<IFeatureFlags>().Ga4Import) return (object?)Results.NotFound();
                // The download is handed to the worker through object storage: without it the import could never finish.
                if (!services.GetRequiredService<IStorageAvailability>().IsAvailable) return (object?)ImportEndpoints.StorageUnavailable();
                return await next(context);
            });

        group.MapPost("/authorize", async (
            Guid siteId,
            ClaimsPrincipal user,
            [FromServices] IImportService importService,
            [FromServices] IGa4OAuthStore store,
            [FromServices] IOptionsMonitor<GoogleOAuthOptions> options,
            [FromServices] TimeProvider timeProvider,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = user.GetRequiredUserId();
            if (await ImportEndpoints.CheckSiteAndPlanAsync(http, userId, siteId, ct) is { } denied) return denied;
            if (await importService.HasActiveImport(new SiteId(siteId)))
                return Results.Conflict("An import job is already in progress for this site.");
            if (NotConfigured(options.CurrentValue) is { } unavailable) return unavailable;

            var state = RandomToken();
            var codeVerifier = RandomToken();
            await store.SaveStateAsync(state, new Ga4StateRecord(userId, siteId, codeVerifier, timeProvider.GetUtcNow()), ct);

            var o = options.CurrentValue;
            var url = QueryHelpers.AddQueryString(AuthorizationEndpoint, new Dictionary<string, string?>
            {
                ["client_id"] = o.ClientId,
                ["redirect_uri"] = o.RedirectUri,
                ["response_type"] = "code",
                ["scope"] = Scope,
                ["access_type"] = "online",
                ["prompt"] = "consent",
                ["state"] = state,
                ["code_challenge"] = CodeChallenge(codeVerifier),
                ["code_challenge_method"] = "S256"
            });
            return Results.Ok(new Ga4AuthorizeResponse(url));
        });

        group.MapPost("/connect", async (
            Guid siteId,
            Ga4ConnectRequest request,
            ClaimsPrincipal user,
            [FromServices] IGa4OAuthStore store,
            [FromServices] IGoogleAnalyticsClient google,
            [FromServices] IOptionsMonitor<GoogleOAuthOptions> options,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = user.GetRequiredUserId();
            if (await ImportEndpoints.CheckSiteAndPlanAsync(http, userId, siteId, ct) is { } denied) return denied;
            if (NotConfigured(options.CurrentValue) is { } unavailable) return unavailable;
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State) || request.Code.Length > 2048)
                return BadRequest(ExpiredMessage);

            // Single use: the state is removed whatever happens next.
            var state = await store.TakeStateAsync(request.State, ct);
            if (state == null || state.UserId != userId || state.SiteId != siteId)
                return BadRequest(ExpiredMessage);

            try
            {
                var token = await google.ExchangeCodeAsync(request.Code, state.CodeVerifier, ct);
                if (token.Scope != null && !token.Scope.Split(' ').Contains(Scope))
                    return BadRequest("Tempoo needs read access to Google Analytics. Connect with Google again and allow it.");

                var properties = await google.ListPropertiesAsync(token.Value, ct);
                if (properties.Count == 0)
                    return Results.Ok(new Ga4ConnectResponse("", []));

                var connectionId = RandomToken();
                await store.SaveConnectionAsync(connectionId,
                    new Ga4ConnectionRecord(userId, siteId, token.Value, [.. properties.Select(p => p.Id)], token.ExpiresAt), ct);

                return Results.Ok(new Ga4ConnectResponse(connectionId, [.. properties.Select(p => new Ga4PropertyDto(p.Id, p.Name, p.Account))]));
            }
            catch (GoogleApiException ex)
            {
                return BadRequest(ex.Message);
            }
        });

        group.MapPost("/", async (
            Guid siteId,
            Ga4ImportRequest request,
            ClaimsPrincipal user,
            [FromServices] IImportService importService,
            [FromServices] IGa4OAuthStore store,
            [FromServices] Ga4DownloadQueue downloads,
            [FromServices] IOptionsMonitor<GoogleOAuthOptions> options,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = user.GetRequiredUserId();
            if (await ImportEndpoints.CheckSiteAndPlanAsync(http, userId, siteId, ct) is { } denied) return denied;
            if (NotConfigured(options.CurrentValue) is { } unavailable) return unavailable;
            if (string.IsNullOrWhiteSpace(request.ConnectionId) || !Ga4PropertyId.IsValid(request.PropertyId))
                return BadRequest(ExpiredMessage);

            var connection = await store.TakeConnectionAsync(request.ConnectionId, ct);
            if (connection == null || connection.UserId != userId || connection.SiteId != siteId)
                return BadRequest(ExpiredMessage);
            if (!connection.PropertyIds.Contains(request.PropertyId))
                return BadRequest("That Google Analytics property was not offered for this connection. Connect with Google again.");

            Guid jobId;
            try
            {
                jobId = await importService.CreateGa4Job(new SiteId(siteId), request.PropertyId!, new UserId(userId));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }

            if (!downloads.TryQueue(new Ga4DownloadRequest(jobId, new SiteId(siteId), request.PropertyId!, connection.AccessToken)))
            {
                const string busy = "Too many Google Analytics imports are running right now. Try again in a few minutes.";
                await importService.MarkFailed(jobId, busy);
                return Results.Json(new ErrorMessagePayload(busy), AppJsonContext.Default.ErrorMessagePayload, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Accepted($"/api/sites/{siteId}/imports/{jobId}", new JobIdResponse(jobId));
        });
    }

    private static IResult? NotConfigured(GoogleOAuthOptions options) => options.IsConfigured
        ? null
        : Results.Json(
            new ErrorMessagePayload("Google Analytics import is not configured on this server."),
            AppJsonContext.Default.ErrorMessagePayload,
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult BadRequest(string message) =>
        Results.Json(new ErrorMessagePayload(message), AppJsonContext.Default.ErrorMessagePayload, statusCode: StatusCodes.Status400BadRequest);

    private static string RandomToken() => Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>PKCE S256: base64url(SHA-256(code_verifier)).</summary>
    public static string CodeChallenge(string codeVerifier) =>
        Base64UrlTextEncoder.Encode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(codeVerifier)));
}
