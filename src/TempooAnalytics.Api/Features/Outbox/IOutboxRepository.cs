namespace TempooAnalytics.Api.Features.Outbox;

public interface IOutboxRepository
{
    Task SaveOutboxMessageAsync(string type, object payload);
}
