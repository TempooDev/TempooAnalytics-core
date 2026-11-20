using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Plans;

/// <summary>The plan policy of the core: <see cref="CommunityPlanPolicy"/>. The tier policy of the cloud has its own (private) tests.</summary>
public class PlanPolicyTests
{
    private readonly Mock<ISiteRepository> _sites = new();
    private readonly UserId _user = UserId.New();

    [Fact]
    public async Task Community_HasNoLimitsAndAllFeatures()
    {
        var policy = new CommunityPlanPolicy();

        (await policy.MaxSitesAsync(_user, CancellationToken.None)).Should().BeNull();
        (await policy.HasAdvancedAnalyticsAsync(_user, CancellationToken.None)).Should().BeTrue();
        (await policy.CanCreateSiteAsync(_user, CancellationToken.None)).Should().BeTrue();
        _sites.VerifyNoOtherCalls();
    }
}
