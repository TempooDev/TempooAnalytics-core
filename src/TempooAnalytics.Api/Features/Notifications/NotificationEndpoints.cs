using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;

namespace TempooAnalytics.Api.Features.Notifications;

public record NotificationDto(
    Guid Id,
    string Type,
    int Severity,
    string Title,
    string? Body,
    string? Link,
    Guid? SiteId,
    DateTime CreatedAt,
    DateTime? ReadAt);

public record NotificationPageDto(IReadOnlyList<NotificationDto> Items, string? NextCursor);

public record UnreadCountDto(int Count);

/// <summary>
/// The signed-in user's notification inbox. Everything is filtered by the token's user, so no site check
/// is needed: a notification of another user behaves as if it didn't exist.
/// </summary>
public static class NotificationEndpoints
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/notifications")
            .WithTags("Notifications")
            .RequireAuthorization()
            // Not released yet: behave as if the endpoints didn't exist.
            .AddEndpointFilter(async (context, next) =>
                context.HttpContext.RequestServices.GetRequiredService<IFeatureFlags>().Notifications
                    ? await next(context)
                    : Results.NotFound());

        group.MapGet("/", async (
            [FromQuery] string? cursor,
            [FromQuery] int? limit,
            ClaimsPrincipal user,
            AnalyticsDbContext db,
            CancellationToken ct) =>
        {
            var userId = new UserId(user.GetRequiredUserId());
            var take = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);

            var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
            if (cursor != null)
            {
                if (!TryDecodeCursor(cursor, out var createdAt, out var id))
                    return Results.Json(new ErrorMessagePayload("Invalid cursor."), AppJsonContext.Default.ErrorMessagePayload, statusCode: StatusCodes.Status400BadRequest);
                // Row comparison (created_at, id) < (cursor), same order as the list and its index.
                query = query.Where(n => EF.Functions.LessThan(ValueTuple.Create(n.CreatedAt, n.Id), ValueTuple.Create(createdAt, id)));
            }

            // One extra row tells whether there is a next page.
            var rows = await query
                .OrderByDescending(n => n.CreatedAt)
                .ThenByDescending(n => n.Id)
                .Take(take + 1)
                .Select(n => new NotificationDto(
                    n.Id, n.Type, (int)n.Severity, n.Title, n.Body, n.Link,
                    n.SiteId.HasValue ? n.SiteId.Value.Value : null,
                    n.CreatedAt, n.ReadAt))
                .ToListAsync(ct);

            var items = rows.Take(take).ToList();
            var next = rows.Count > take ? EncodeCursor(items[^1].CreatedAt, items[^1].Id) : null;
            return Results.Ok(new NotificationPageDto(items, next));
        });

        group.MapGet("/unread-count", async (ClaimsPrincipal user, AnalyticsDbContext db, CancellationToken ct) =>
        {
            var userId = new UserId(user.GetRequiredUserId());
            var count = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
            return Results.Ok(new UnreadCountDto(count));
        });

        group.MapPost("/{id:guid}/read", async (
            Guid id,
            ClaimsPrincipal user,
            AnalyticsDbContext db,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var userId = new UserId(user.GetRequiredUserId());
            if (!await db.Notifications.AnyAsync(n => n.Id == id && n.UserId == userId, ct))
                return Results.NotFound();

            // An already read notification keeps its first read date.
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await db.Notifications
                .Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
            return Results.NoContent();
        });

        group.MapPost("/read-all", async (
            ClaimsPrincipal user,
            AnalyticsDbContext db,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var userId = new UserId(user.GetRequiredUserId());
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await db.Notifications
                .Where(n => n.UserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
            return Results.NoContent();
        });
    }

    /// <summary>base64url("{createdAt:O}|{id}") of the last item of the page.</summary>
    public static string EncodeCursor(DateTime createdAt, Guid id) =>
        Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes($"{DateTime.SpecifyKind(createdAt, DateTimeKind.Utc):O}|{id}"));

    public static bool TryDecodeCursor(string cursor, out DateTime createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        if (cursor.Length is 0 or > 200) return false;
        try
        {
            var parts = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(cursor)).Split('|');
            if (parts.Length != 2 || !Guid.TryParse(parts[1], out id)) return false;
            if (!DateTime.TryParseExact(parts[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out createdAt)) return false;
            createdAt = createdAt.ToUniversalTime();
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
