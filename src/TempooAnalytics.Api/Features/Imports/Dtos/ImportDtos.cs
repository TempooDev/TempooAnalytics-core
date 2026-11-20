using TempooAnalytics.Api.Features.Imports.Models;

namespace TempooAnalytics.Api.Features.Imports.Dtos;

public record ImportJobDto(
    Guid Id,
    Guid SiteId,
    ImportType Type,
    ImportStatus Status,
    DateTime CreatedAt,
    long RowsProcessed,
    string? ErrorMessage,
    DateTime? StartedAt,
    DateTime? CompletedAt
);
