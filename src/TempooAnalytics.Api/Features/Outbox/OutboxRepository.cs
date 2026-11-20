using System.Text.Json;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Data;

namespace TempooAnalytics.Api.Features.Outbox;

public class OutboxRepository(AnalyticsDbContext dbContext) : IOutboxRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveOutboxMessageAsync(string type, object payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var outboxMessage = new OutboxMessage
        {
            Type = type,
            Payload = JsonDocument.Parse(payloadJson),
            CreatedAt = DateTime.UtcNow
        };
        dbContext.OutboxMessages.Add(outboxMessage);
        await dbContext.SaveChangesAsync();
    }
}
