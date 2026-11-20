using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public interface IPublicPreviewService : IService
{
    Task<string?> GeneratePublicTokenAsync(UserId userId, string siteKey, CancellationToken ct);
    Task<PublicPreviewResponse?> GetPublicPreviewDataAsync(string token, DateTime? from = null, DateTime? to = null, int days = 30, string? breakdown = null, CancellationToken ct = default);
}
