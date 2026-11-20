using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>
/// The per-site metric endpoints accept a site key or the site's UUID. Looking up by UUID used to fail
/// with a 500 because EF couldn't translate `s.Id.Value`.
/// </summary>
public class SiteMetricsLookupIntegrationTests : FunctionalTestBase
{
    private readonly Mock<IClickHouseRepository> _clickHouse = new();
    private readonly HttpClient _client;

    public SiteMetricsLookupIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _clickHouse
            .Setup(r => r.GetSiteLocationsAsync(It.IsAny<IEnumerable<SiteId>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<AnalyticsFilterDto?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LocationDto> { new() { CountryCode = "ES", Value = 3 } });

        _client = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClickHouseRepository>();
            services.AddScoped(_ => _clickHouse.Object);
        })).CreateClient();
    }

    [Fact]
    public async Task Locations_BySiteUuid_ReturnsOwnSiteData()
    {
        var ownerId = Guid.NewGuid();
        var siteId = await AddSiteAsync(ownerId, "uuid-lookup");

        AuthenticateClient(_client, ownerId, false);
        var response = await _client.GetAsync($"/api/analytics/sites/{siteId.Value}/locations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<LocationDto>>()).Should().ContainSingle(l => l.CountryCode == "ES");
        VerifyQueriedSite(siteId);
    }

    [Fact]
    public async Task Locations_BySiteKey_ReturnsOwnSiteData()
    {
        var ownerId = Guid.NewGuid();
        var siteId = await AddSiteAsync(ownerId, "key-lookup");

        AuthenticateClient(_client, ownerId, false);
        var response = await _client.GetAsync("/api/analytics/sites/key-lookup/locations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        VerifyQueriedSite(siteId);
    }

    [Fact]
    public async Task Locations_ForAnotherUsersSite_ReturnsNoData()
    {
        var siteId = await AddSiteAsync(Guid.NewGuid(), "someone-elses");

        AuthenticateClient(_client, Guid.NewGuid(), false);
        var byUuid = await _client.GetAsync($"/api/analytics/sites/{siteId.Value}/locations");
        var byKey = await _client.GetAsync("/api/analytics/sites/someone-elses/locations");

        byUuid.StatusCode.Should().Be(HttpStatusCode.OK);
        (await byUuid.Content.ReadFromJsonAsync<List<LocationDto>>()).Should().BeEmpty();
        (await byKey.Content.ReadFromJsonAsync<List<LocationDto>>()).Should().BeEmpty();
        _clickHouse.Verify(r => r.GetSiteLocationsAsync(It.IsAny<IEnumerable<SiteId>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<AnalyticsFilterDto?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void VerifyQueriedSite(SiteId siteId) =>
        _clickHouse.Verify(r => r.GetSiteLocationsAsync(
            It.Is<IEnumerable<SiteId>>(ids => ids.Single() == siteId),
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<AnalyticsFilterDto?>(), It.IsAny<CancellationToken>()), Times.Once);

    private async Task<SiteId> AddSiteAsync(Guid ownerId, string siteKey)
    {
        var siteId = SiteId.New();
        using var context = Fixture.CreateContext();
        context.AnalyticsSites.Add(new AnalyticsSite
        {
            Id = siteId,
            SiteKey = siteKey,
            Name = siteKey,
            OwnerId = new UserId(ownerId),
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return siteId;
    }
}
