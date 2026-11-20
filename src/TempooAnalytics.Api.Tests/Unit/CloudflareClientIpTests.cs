using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Http;
using TempooAnalytics.ServiceDefaults;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit;

public class CloudflareClientIpTests
{
    private static readonly IReadOnlyList<IPNetwork> Ranges = CloudflareClientIp.ParseRanges(CloudflareClientIp.DefaultRanges);

    private const string CloudflareEdge = "172.70.1.10"; // inside 172.64.0.0/13
    private const string Visitor = "198.51.100.5";
    private const string TraefikContainer = "10.0.1.7";

    private static DefaultHttpContext Request(string? forwardedFor, params (string Name, string Value)[] headers)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(TraefikContainer);
        if (forwardedFor is not null) ctx.Request.Headers["X-Forwarded-For"] = forwardedFor;
        foreach (var (name, value) in headers) ctx.Request.Headers[name] = value;
        return ctx;
    }

    [Fact]
    public void ThroughCloudflare_UsesCfConnectingIp_AndKeepsCountry()
    {
        var ctx = Request(CloudflareEdge, ("CF-Connecting-IP", Visitor), ("CF-IPCountry", "ES"));

        var client = CloudflareClientIp.Apply(ctx, Ranges);

        Assert.Equal(Visitor, client?.ToString());
        Assert.Equal(Visitor, ctx.Connection.RemoteIpAddress?.ToString());
        Assert.Equal(Visitor, ctx.Request.Headers["X-Forwarded-For"].ToString());
        Assert.Equal(Visitor, ctx.Request.Headers["CF-Connecting-IP"].ToString());
        Assert.Equal("ES", ctx.Request.Headers["CF-IPCountry"].ToString());
    }

    [Fact]
    public void ThroughCloudflare_WhenTraefikKeepsClientChain_TrustsOnlyTheRightmostHop()
    {
        // Client forged "1.1.1.1", Cloudflare appended the visitor, Traefik appended the edge.
        var ctx = Request($"1.1.1.1, {Visitor}, {CloudflareEdge}", ("CF-Connecting-IP", Visitor));

        Assert.Equal(Visitor, CloudflareClientIp.Apply(ctx, Ranges)?.ToString());
    }

    [Fact]
    public void DirectToOrigin_IgnoresForgedCloudflareHeaders()
    {
        var ctx = Request(Visitor, ("CF-Connecting-IP", "8.8.8.8"), ("CF-IPCountry", "US"));

        var client = CloudflareClientIp.Apply(ctx, Ranges);

        Assert.Equal(Visitor, client?.ToString());
        Assert.False(ctx.Request.Headers.ContainsKey("CF-Connecting-IP"));
        Assert.False(ctx.Request.Headers.ContainsKey("CF-IPCountry"));
        Assert.Equal(Visitor, ctx.Request.Headers["X-Forwarded-For"].ToString());
    }

    [Fact]
    public void DirectToOrigin_IgnoresForgedLeftmostForwardedFor()
    {
        var ctx = Request($"8.8.8.8, {Visitor}");

        Assert.Equal(Visitor, CloudflareClientIp.Apply(ctx, Ranges)?.ToString());
    }

    [Fact]
    public void AlwaysDropsClientControlledGeoHeaders()
    {
        var ctx = Request(CloudflareEdge, ("CF-Connecting-IP", Visitor), ("X-Country", "US"), ("X-Geoip-Country", "US"), ("True-Client-IP", "8.8.8.8"));

        CloudflareClientIp.Apply(ctx, Ranges);

        Assert.False(ctx.Request.Headers.ContainsKey("X-Country"));
        Assert.False(ctx.Request.Headers.ContainsKey("X-Geoip-Country"));
        Assert.False(ctx.Request.Headers.ContainsKey("True-Client-IP"));
    }

    [Fact]
    public void WithoutForwardedFor_FallsBackToConnection()
    {
        var ctx = Request(null, ("CF-Connecting-IP", "8.8.8.8"));

        Assert.Equal(TraefikContainer, CloudflareClientIp.Apply(ctx, Ranges)?.ToString());
        Assert.False(ctx.Request.Headers.ContainsKey("CF-Connecting-IP"));
    }

    [Theory]
    [InlineData("2606:4700:10::6816:1", true)]
    [InlineData("::ffff:104.16.1.1", true)]
    [InlineData("104.15.255.255", false)]
    [InlineData("203.0.113.23", false)]
    public void IsCloudflare_MatchesPublishedRanges(string ip, bool expected)
    {
        Assert.Equal(expected, CloudflareClientIp.IsCloudflare(IPAddress.Parse(ip), Ranges));
    }
}
