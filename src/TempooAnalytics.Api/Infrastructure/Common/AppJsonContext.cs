using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Endpoints;
using TempooAnalytics.Api.Features.Imports.Dtos;
using TempooAnalytics.Api.Features.Imports.Endpoints;
using TempooAnalytics.Api.Infrastructure.Dtos;

namespace TempooAnalytics.Api.Infrastructure.Common;

/// <summary>
/// JSON serialization context for Native AOT.
/// Add here every type that is sent to Redis or stored as JSON in the DB.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExportTaskPayload))]
[JsonSerializable(typeof(ImportTaskPayload))]
[JsonSerializable(typeof(NotificationPayload))]
[JsonSerializable(typeof(AdminAlertRequest))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(AnalyticsEventDto))]
[JsonSerializable(typeof(AnalyticsFilterDto))]
[JsonSerializable(typeof(AdvancedFilterDto))]
[JsonSerializable(typeof(ViewportDto))]
[JsonSerializable(typeof(List<AdvancedFilterDto>), TypeInfoPropertyName = "ListAdvancedFilterDto")]
[JsonSerializable(typeof(ErrorMessagePayload))]
[JsonSerializable(typeof(SuccessMessagePayload))]
[JsonSerializable(typeof(ExportDataRequest))]
[JsonSerializable(typeof(ExportStatusResponse))]
[JsonSerializable(typeof(ExportJobStatusDto))]
[JsonSerializable(typeof(ImportJobDto))]
[JsonSerializable(typeof(IEnumerable<ImportJobDto>))]
[JsonSerializable(typeof(List<ImportJobDto>))]
[JsonSerializable(typeof(JobIdResponse))]
[JsonSerializable(typeof(TempooAnalytics.Api.Features.Imports.Ga4.Ga4AuthorizeResponse))]
[JsonSerializable(typeof(TempooAnalytics.Api.Features.Imports.Ga4.Ga4ConnectRequest))]
[JsonSerializable(typeof(TempooAnalytics.Api.Features.Imports.Ga4.Ga4ConnectResponse))]
[JsonSerializable(typeof(TempooAnalytics.Api.Features.Imports.Ga4.Ga4ImportRequest))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
[JsonSerializable(typeof(TempooAnalytics.Api.Features.Notifications.NotificationPageDto))]
[JsonSerializable(typeof(TempooAnalytics.Api.Features.Notifications.UnreadCountDto))]
[JsonSerializable(typeof(PublicPreviewResponse))]
[JsonSerializable(typeof(PublicMetricDto))]
[JsonSerializable(typeof(PublicTopPageDto))]
[JsonSerializable(typeof(TempooAnalytics.Analytics.Query.SiteMetricsSummaryDto), TypeInfoPropertyName = "PublicPreviewSiteMetricsSummaryDto")]
[JsonSerializable(typeof(TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard.LocationDto), TypeInfoPropertyName = "PublicPreviewLocationDto")]
[JsonSerializable(typeof(TempooAnalytics.Api.Features.Analytics.Dtos.HourlyActivityDto), TypeInfoPropertyName = "PublicPreviewHourlyActivityDto")]
[JsonSerializable(typeof(TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard.DeviceDto), TypeInfoPropertyName = "PublicPreviewDeviceDto")]
[JsonSerializable(typeof(TempooAnalytics.Api.Features.Analytics.Dtos.Funnels.FunnelDefinitionDto), TypeInfoPropertyName = "PublicPreviewFunnelDefinitionDto")]
public partial class AppJsonContext : JsonSerializerContext
{
}

public record ErrorMessagePayload(string Error);
public record SuccessMessagePayload(string Message);

public record ExportTaskPayload(
    string Type,
    string JobId,
    string SiteId,
    string From,
    string To,
    string? Email
);

public record ImportTaskPayload(
    string JobId,
    string SiteId,
    string Type,
    string? PropertyId,
    string? Start,
    string? End,
    string? FilePath
);

/// <summary>
/// Message of <c>queue:notifications</c> (written by the Go worker). Every value is a string.
/// Type: IMPORT_COMPLETED, IMPORT_FAILED, EXPORT_COMPLETED or EXPORT_FAILED.
/// </summary>
public record NotificationPayload(
    string Type,
    string JobId,
    string? SiteId = null,
    string? Email = null,
    string? DownloadUrl = null,
    string? RowsProcessed = null,
    string? ErrorMessage = null
);
