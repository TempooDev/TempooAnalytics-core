using System.Text;
using ClickHouse.Client.ADO;
using Dapper;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Analytics.Query;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Tracing;
using TempooAnalytics.Api.Shared.Telemetry;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public class FunnelRepository(IConfiguration configuration, AnalyticsDbContext dbContext, IFunnelQueries funnelQueries) : IFunnelRepository
{
    private readonly string _clickHouseConnString = configuration.GetConnectionString("clickhouse") 
        ?? throw new InvalidOperationException("ClickHouse connection string is missing");

    private ClickHouseConnection CreateClickHouseConnection() => new(_clickHouseConnString);

    public async Task<EventMetadataDto> GetEventMetadataAsync(SiteId siteId, DateTime start, DateTime end, CancellationToken ct = default)
    {
        using var act = TelemetryExtensions.StartActivity("repo.funnels.metadata");
        
        await using var chConn = CreateClickHouseConnection();
        
        var eventNamesSql = @"
            SELECT DISTINCT event_name FROM tempoo_analytics.events 
            WHERE site_id = @siteId AND event_name NOT IN ('pageview', 'leave') AND timestamp >= @start AND timestamp < @end";
            
        var urlsSql = @"
            SELECT DISTINCT path FROM tempoo_analytics.events 
            WHERE site_id = @siteId AND event_name = 'pageview' AND timestamp >= @start AND timestamp < @end";

        var parameters = new { siteId = siteId.Value, start, end };
        
        var eventNames = (await chConn.QueryAsync<string>(eventNamesSql, parameters)).ToList();
        var urls = (await chConn.QueryAsync<string>(urlsSql, parameters)).ToList();

        return new EventMetadataDto { EventNames = eventNames, Urls = urls };
    }

    public async Task<FunnelResultDto> CalculateFunnelAsync(SiteId siteId, List<FunnelStepDto> steps, DateTime start, DateTime end, CancellationToken ct = default)
    {
        using var act = TelemetryExtensions.StartActivity("repo.funnels.calculate");
        if (steps == null || steps.Count == 0) return new FunnelResultDto();

        // The SQL lives in TempooAnalytics.Analytics.Query, shared with the public API.
        var result = await funnelQueries.CalculateAsync(siteId.Value,
            steps.Select(s => new FunnelStep(s.Order, s.Name, s.Type == FunnelStepType.Pageview, s.Value)).ToList(), start, end, ct);
        return new FunnelResultDto
        {
            Steps = result.Steps.Select(s => new FunnelStepResultDto
            {
                Order = s.Order, Name = s.Name, Sessions = s.Sessions, DropOffRate = s.DropOffRate, ConversionRate = s.ConversionRate
            }).ToList(),
            OverallConversionRate = result.OverallConversionRate
        };
    }

    public async Task<FunnelDefinition?> GetFunnelDefinitionAsync(Guid id, SiteId siteId, CancellationToken ct = default) 
        => await dbContext.Funnels.FirstOrDefaultAsync(f => f.Id == id && f.SiteId == siteId, ct);

    public async Task<Guid> CreateFunnelDefinitionAsync(FunnelDefinition funnel, CancellationToken ct = default) 
    { 
        dbContext.Funnels.Add(funnel); 
        await dbContext.SaveChangesAsync(ct); 
        return funnel.Id; 
    }

    public async Task UpdateFunnelDefinitionAsync(FunnelDefinition funnel, CancellationToken ct = default) 
    { 
        dbContext.Funnels.Update(funnel); 
        await dbContext.SaveChangesAsync(ct); 
    }

    public async Task DeleteFunnelDefinitionAsync(FunnelDefinition funnel, CancellationToken ct = default) 
    { 
        dbContext.Funnels.Remove(funnel); 
        await dbContext.SaveChangesAsync(ct); 
    }

    public async Task<List<FunnelDefinitionDto>> GetFunnelsBySiteAsync(SiteId siteId, CancellationToken ct = default) 
        => await dbContext.Funnels.AsNoTracking()
            .Where(f => f.SiteId == siteId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FunnelDefinitionDto 
            { 
                Id = f.Id, 
                Name = f.Name, 
                SiteId = f.SiteId.ToString(), 
                Steps = f.Steps 
            })
            .ToListAsync(ct);
}
