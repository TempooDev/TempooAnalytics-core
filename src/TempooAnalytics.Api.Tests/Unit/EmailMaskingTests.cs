using TempooAnalytics.Api.Infrastructure.Services;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit;

public class EmailMaskingTests
{
    [Theory]
    [InlineData("alice@example.com", "ali**@example.com")]
    [InlineData("abc@test.io", "abc@test.io")]
    [InlineData("a@b.com", "a@b.com")]
    [InlineData("alice.smith@example.org", "ali********@example.org")]
    [InlineData("ant@domain.com", "ant@domain.com")]
    [InlineData("", "")]
    [InlineData("invalid-email", "invalid-email")]
    public void MaskEmail_ShouldFollowRules(string input, string expected)
    {
        Assert.Equal(expected, AdminAlerts.MaskEmail(input));
    }
}
