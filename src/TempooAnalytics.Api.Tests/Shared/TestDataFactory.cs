using System;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Tests.Shared;

public static class TestDataFactory
{
    public static AnalyticsSite CreateTestSite(string siteKey, Guid ownerId)
    {
        return new AnalyticsSite
        {
            Id = SiteId.New(),
            SiteKey = siteKey,
            Name = siteKey,
            OwnerId = new UserId(ownerId),
            CreatedAt = DateTime.UtcNow
        };
    }

    public static User CreateTestUser(string username, string email)
    {
        return new User
        {
            Id = UserId.New(),
            Username = username,
            Email = email,
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
    }
}
