using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

public class AuthIntegrationTests : FunctionalTestBase
{
    private const string Username = "remember@tempoo.test";
    private const string Password = "correct-horse-battery";

    public AuthIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Login_WithoutRememberMe_SetsSessionCookie()
    {
        await SeedUserAsync();

        var response = await Client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = RefreshCookie(response);
        cookie.Should().NotContain("expires=").And.NotContain("max-age=");
        cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=lax").And.Contain("path=/");
    }

    [Fact]
    public async Task Login_WithRememberMe_SetsCookieExpiringIn30Days()
    {
        await SeedUserAsync();

        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Username, Password, RememberMe: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = RefreshCookie(response);
        cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=lax").And.Contain("path=/");
        var expires = cookie.Split(';').Select(p => p.Trim()).Single(p => p.StartsWith("expires="))["expires=".Length..];
        DateTimeOffset.Parse(expires, CultureInfo.InvariantCulture)
            .Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorizedWithoutCookie()
    {
        await SeedUserAsync();

        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Username, "wrong", RememberMe: true));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeFalse(cookies is null ? "" : string.Join(" | ", cookies));
    }

    [Fact]
    public async Task Refresh_DoesNotResendTheCookie()
    {
        await SeedUserAsync();
        var login = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Username, Password, RememberMe: true));
        var tokens = await login.Content.ReadFromJsonAsync<AuthResponse>();

        // Without cookie handling, so the Cookie header set here is sent as is.
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"refresh_token={tokens!.RefreshToken}");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("refresh_token=")))
            .Should().BeFalse("a remembered session must not be extended by refreshing");
    }

    [Fact]
    public async Task Logout_ExpiresTheCookie()
    {
        var response = await Client.PostAsync("/api/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = RefreshCookie(response);
        cookie.Should().Contain("expires=thu, 01 jan 1970").And.Contain("path=/");
    }

    private static string RefreshCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("refresh_token=")).ToLowerInvariant();

    private async Task SeedUserAsync()
    {
        using var context = Fixture.CreateContext();
        context.Users.Add(new User
        {
            Id = UserId.New(),
            Username = Username,
            Email = Username,
            PasswordHash = PasswordHelper.HashPassword(Password),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await context.SaveChangesAsync();
    }
}
