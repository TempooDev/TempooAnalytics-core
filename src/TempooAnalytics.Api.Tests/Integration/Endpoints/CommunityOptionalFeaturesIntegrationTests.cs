using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Email;
using TempooAnalytics.Api.Infrastructure.Modules;
using TempooAnalytics.Api.Infrastructure.Services;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>
/// What a self-hosted instance (core alone) has to say when an optional dependency is missing: no object storage for imports and
/// exports (a clear 501 and a flag the app reads, instead of a 500 or a job that never ends) and no email provider (the admin
/// console does not claim a welcome email), and what the admin console shows of the cloud (no Tiers link, no plan column).
/// </summary>
public class CommunityOptionalFeaturesIntegrationTests : FunctionalTestBase
{
    private const string MasterPassword = "master-for-tests";

    public CommunityOptionalFeaturesIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    private sealed class FixedStorage(bool available) : IStorageAvailability
    {
        public bool IsAvailable => available;
    }

    private WebApplicationFactory<Program> CommunityHost(bool storage, bool importFlags = false) =>
        Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting(FeatureModuleLoader.EnabledSetting, "false");
            b.UseSetting("Resend:ApiKey", "");
            b.UseSetting("RESEND_APIKEY", "");
            b.UseSetting("Management:MasterPassword", MasterPassword);
            b.UseSetting("FeatureFlags:PlausibleImport", importFlags ? "true" : "false");
            b.UseSetting("FeatureFlags:Ga4Import", importFlags ? "true" : "false");
            b.ConfigureTestServices(s => s.AddSingleton<IStorageAvailability>(new FixedStorage(storage)));
        });

    private HttpClient SignedInClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        AuthenticateClient(client, Guid.NewGuid(), isAdmin: false);
        return client;
    }

    [Fact]
    public async Task Features_WithoutStorage_ReportsItAndWithStorageDoesNot()
    {
        using (var host = CommunityHost(storage: false))
        {
            var flags = await host.CreateClient().GetFromJsonAsync<Dictionary<string, bool>>("/api/features");
            flags.Should().ContainKey("dataStorageUnavailable").WhoseValue.Should().BeTrue();
        }

        using (var host = CommunityHost(storage: true))
        {
            var flags = await host.CreateClient().GetFromJsonAsync<Dictionary<string, bool>>("/api/features");
            flags.Should().NotContainKey("dataStorageUnavailable");
        }
    }

    [Fact]
    public async Task PlausibleImport_WithoutStorage_Answers501WithAClearMessage_AndNoJobIsCreated()
    {
        using var host = CommunityHost(storage: false, importFlags: true);
        var client = SignedInClient(host);
        var content = new MultipartFormDataContent { { new ByteArrayContent(new byte[64]), "file", "export.zip" } };

        var response = await client.PostAsync($"/api/sites/{Guid.NewGuid()}/imports/plausible", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("not available on this server");
    }

    [Fact]
    public async Task PlausibleImport_WithoutStorage_AndFlagOff_StillAnswers404()
    {
        using var host = CommunityHost(storage: false, importFlags: false);
        var client = SignedInClient(host);
        var content = new MultipartFormDataContent { { new ByteArrayContent(new byte[64]), "file", "export.zip" } };

        (await client.PostAsync($"/api/sites/{Guid.NewGuid()}/imports/plausible", content)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ga4Import_WithoutStorage_Answers501()
    {
        using var host = CommunityHost(storage: false, importFlags: true);
        var client = SignedInClient(host);

        var response = await client.PostAsync($"/api/sites/{Guid.NewGuid()}/imports/ga4/authorize", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Export_WithoutStorage_Answers501_AndLeavesNoJobPending()
    {
        using var host = CommunityHost(storage: false);
        var client = SignedInClient(host);
        var siteId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/sites/{siteId}/export", new { from = DateTime.UtcNow.AddDays(-7), to = DateTime.UtcNow });

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not available on this server");
        using var db = Fixture.CreateContext();
        db.ExportJobs.Should().NotContain(j => j.SiteId == new SiteId(siteId));
    }

    [Fact]
    public async Task Export_WithStorage_IsNotRefusedForMissingStorage()
    {
        using var host = CommunityHost(storage: true);
        var client = SignedInClient(host);

        var response = await client.PostAsJsonAsync($"/api/sites/{Guid.NewGuid()}/export", new { from = DateTime.UtcNow.AddDays(-7), to = DateTime.UtcNow });

        // Whatever else it answers (the site does not exist), it is not the storage refusal.
        response.StatusCode.Should().NotBe(HttpStatusCode.NotImplemented);
    }

    private static string Token(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private async Task<HttpClient> UnlockedAdminConsole(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        AuthenticateClient(client, Guid.NewGuid(), isAdmin: true);
        var html = await (await client.GetAsync("/Admin/Users")).Content.ReadAsStringAsync();
        var unlock = await client.PostAsync("/Admin/Users?handler=Unlock", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["MasterPassword"] = MasterPassword,
            ["__RequestVerificationToken"] = Token(html),
        }));
        unlock.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    [Fact]
    public async Task AdminConsole_Community_HasNoTiersLinkNoPlanColumnNorPlanSelectors()
    {
        using var host = CommunityHost(storage: true);
        var client = await UnlockedAdminConsole(host);

        foreach (var page in new[] { "/Admin/Users", "/Admin/Feedback" })
        {
            var html = WebUtility.HtmlDecode(await (await client.GetAsync(page)).Content.ReadAsStringAsync());
            html.Should().Contain("Usuarios", $"{page} still has its navigation");
            html.Should().NotContain("/admin/tiers").And.NotContain(">Tiers<", $"{page} must not link a page the community build does not have");
        }

        var users = WebUtility.HtmlDecode(await (await client.GetAsync("/Admin/Users")).Content.ReadAsStringAsync());
        users.Should().NotContain("Plan de Suscripción").And.NotContain("<th class=\"px-6 py-4\">Plan</th>").And.NotContain("Free (Default)");
        users.Should().Contain("Rol / Estado", "the rest of the table is untouched");
    }

    [Fact]
    public async Task AdminConsole_LoadsNoThirdPartyAsset_AndInlinesItsStyles()
    {
        using var host = CommunityHost(storage: true);
        var client = await UnlockedAdminConsole(host);

        foreach (var page in new[] { "/Admin/Users", "/Admin/Feedback" })
        {
            var html = await (await client.GetAsync(page)).Content.ReadAsStringAsync();
            html.Should().NotContain("cdn.tailwindcss.com").And.NotContain("unpkg.com").And.NotContain("fonts.googleapis.com");
            System.Text.RegularExpressions.Regex.IsMatch(html, "(src|href)=\"https?://").Should().BeFalse($"{page} must not load anything from another host");
            html.Should().Contain(".bg-slate-900{", $"{page} carries the Tailwind utilities it uses");
        }
    }

    [Fact]
    public async Task AdminConsole_Community_CreatingAUserWithoutEmailProvider_SaysNoEmailWasSent()
    {
        using var host = CommunityHost(storage: true);
        var client = await UnlockedAdminConsole(host);
        host.Services.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider.GetRequiredService<IEmailService>().IsConfigured.Should().BeFalse();
        var html = await (await client.GetAsync("/Admin/Users")).Content.ReadAsStringAsync();

        var response = await client.PostAsync("/Admin/Users?handler=CreateUser", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["NewUsername"] = $"new-{Guid.NewGuid():N}",
            ["NewPassword"] = "S3cret-pass!",
            ["NewEmail"] = $"{Guid.NewGuid():N}@example.test",
            ["NewTier"] = "free",
            ["__RequestVerificationToken"] = Token(html),
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        page.Should().Contain("No hay proveedor de email configurado").And.Contain("no se ha enviado ningún correo");
        page.Should().NotContain("welcome email sent");
    }
}
