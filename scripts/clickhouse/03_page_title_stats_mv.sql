-- Migration v002: Add Materialized View for Page Titles
-- Reuses the dimension_stats table to store page title metrics efficiently.

CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.page_title_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT 
    site_id, 
    toDate(timestamp) as date, 
    'page_title' as dimension_name, 
    page_title as dimension_value, 
    uniqState(user_hash) as unique_visitors, 
    count() as pageviews
FROM tempoo_analytics.events 
WHERE event_name = 'pageview' 
GROUP BY site_id, date, dimension_name, dimension_value;
