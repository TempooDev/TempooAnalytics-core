using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace TempooAnalytics.Api.Tests.Shared.Validation;

public class MetricsRepositoryHelpersTests
{
    private static readonly Regex KeyRegex = new("^[a-zA-Z0-9_]+$");

    [Theory]
    [InlineData("a")]
    [InlineData("A1_")]
    [InlineData("abc123")]
    public void KeyRegex_Accepts_valid_keys(string key)
    {
        KeyRegex.IsMatch(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("a-b")]
    [InlineData("a b")]
    [InlineData("$")]
    [InlineData("")]
    public void KeyRegex_Rejects_invalid_keys(string key)
    {
        KeyRegex.IsMatch(key).Should().BeFalse();
    }

    [Fact]
    public void SplitAndTrimKeys_Parses_list_correctly()
    {
        var raw = " k1, k2 ,,k3 ";
        var keys = raw.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();

        keys.Should().BeEquivalentTo(new[] { "k1", "k2", "k3" });
    }

    [Fact]
    public void Filter_whitelist_applies_correctly()
    {
        var filters = new Dictionary<string, string>
        {
            ["ok"] = "v",
            ["bad-key"] = "v2",
            ["also_ok"] = "v3"
        };

        var allowed = filters.Keys.Where(k => KeyRegex.IsMatch(k)).ToArray();

        allowed.Should().BeEquivalentTo(new[] { "ok", "also_ok" });
    }
}
