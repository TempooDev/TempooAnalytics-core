namespace TempooAnalytics.Api.Features.Auth.Dtos;

public record InvoiceDto(
    string Id,
    string OrderId,
    long Amount,
    string Currency,
    string Status,
    string Url,
    long CreatedAtUnix);
