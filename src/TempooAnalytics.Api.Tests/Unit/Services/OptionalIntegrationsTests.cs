using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TempooAnalytics.Api.Infrastructure.Email;
using TempooAnalytics.Api.Infrastructure.Services;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Services;

/// <summary>Resend and the owner's alert bot are optional: unconfigured, nothing is called and no personal default exists.</summary>
public class OptionalIntegrationsTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void ResendOptions_HaveNoDefaultSender_NorAnyPersonalAddress()
    {
        var options = new ResendOptions();

        options.FromEmail.Should().BeEmpty();
        options.ApiKey.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "Tempoo <no-reply@example.test>")]
    [InlineData("re_key", "")]
    [InlineData("", "")]
    public async Task Resend_WithoutKeyOrSender_IsNotConfigured_AndSendsNothing(string apiKey, string from)
    {
        var handler = new RecordingHandler();
        var logger = new ListLogger<ResendEmailService>();
        var service = new ResendEmailService(new HttpClient(handler), Options.Create(new ResendOptions { ApiKey = apiKey, FromEmail = from }), logger);

        service.IsConfigured.Should().BeFalse();
        await service.SendEmailAsync("someone@example.test", "subject", "<p>x</p>");
        await service.SendWelcomeEmailAsync("someone@example.test", "someone");
        await service.SendWelcomeWithCredentialsEmailAsync("someone@example.test", "someone", "pw");

        handler.Requests.Should().BeEmpty("no call to api.resend.com is made without a key and a sender");
        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
        logger.Entries.Should().NotContain(e => e.Message.Contains("MISSING"));
    }

    [Fact]
    public void Resend_Unconfigured_SaysSoAtMostOnce_AtInformationLevel()
    {
        var logger = new ListLogger<ResendEmailService>();
        for (var i = 0; i < 5; i++)
            _ = new ResendEmailService(new HttpClient(new RecordingHandler()), Options.Create(new ResendOptions()), logger);

        // The notice is process-wide: another test may already have shown it, so "at most one", never one per construction.
        logger.Entries.Count(e => e.Message.Contains("Email is disabled")).Should().BeLessThanOrEqualTo(1);
        logger.Entries.Should().OnlyContain(e => e.Level <= LogLevel.Information);
    }

    [Fact]
    public async Task Resend_WithKeyAndSender_IsConfigured_AndPostsToResend()
    {
        var handler = new RecordingHandler();
        var service = new ResendEmailService(new HttpClient(handler), Options.Create(new ResendOptions { ApiKey = "re_test", FromEmail = "Tempoo <no-reply@example.test>" }),
            new ListLogger<ResendEmailService>());

        service.IsConfigured.Should().BeTrue();
        await service.SendEmailAsync("someone@example.test", "subject", "<p>x</p>");

        handler.Requests.Should().ContainSingle().Which.RequestUri!.Host.Should().Be("api.resend.com");
    }

    private static AdminAlertService AlertService(RecordingHandler handler, string botUrl)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));
        return new AdminAlertService(factory.Object, Options.Create(new AdminAlertOptions { BotUrl = botUrl }), new ListLogger<AdminAlertService>());
    }

    [Fact]
    public async Task AdminAlerts_WithoutBotUrl_SendNothing_SoUserFeedbackNeverLeavesTheInstance()
    {
        var handler = new RecordingHandler();

        await AlertService(handler, "").AlertAsync(AdminAlerts.FeedbackSubmitted("alice", "title", "private description"));
        await Task.Delay(200);

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AdminAlerts_WithABotUrl_PostToIt()
    {
        var handler = new RecordingHandler();

        await AlertService(handler, "https://alerts.example.test/notify").AlertAsync(AdminAlerts.FeedbackSubmitted("alice", "title", "d"));
        for (var i = 0; i < 50 && handler.Requests.Count == 0; i++) await Task.Delay(50);

        handler.Requests.Should().ContainSingle().Which.RequestUri!.ToString().Should().Be("https://alerts.example.test/notify");
    }

    [Fact]
    public void AdminAlertOptions_HaveNoDefaultDestination() => new AdminAlertOptions().BotUrl.Should().BeEmpty();
}
