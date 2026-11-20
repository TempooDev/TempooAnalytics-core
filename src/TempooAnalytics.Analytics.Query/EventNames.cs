namespace TempooAnalytics.Analytics.Query;

/// <summary>
/// The event_name values the tracker sends on its own. Everything else is a custom event or goal of the site.
/// "leave" (time on page and scroll depth, sent when the page is hidden or the route changes) is internal: it
/// must not show up as a custom event nor count as an event or a view.
/// </summary>
public static class EventNames
{
    public const string Pageview = "pageview";
    public const string Leave = "leave";

    /// <summary>SQL condition on tempoo_analytics.events for the site's own events (custom events and goals).</summary>
    public const string CustomEventCondition = "event_name NOT IN ('pageview', 'leave')";

    /// <summary>
    /// SQL aggregate expression (inside a GROUP BY session_id) that is true when the session is a bounce: exactly
    /// one pageview and no interactive event (custom event or goal). The automatic "leave" does not count, and a
    /// session with no pageview at all is not a bounce. Heatmap clicks live in their own table, not in events.
    /// </summary>
    public const string BouncedSessionCondition =
        "(countIf(event_name = 'pageview') = 1 AND countIf(" + CustomEventCondition + ") = 0)";
}
