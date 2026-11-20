-- 1. Drop existing MVs (try both names just in case)
DROP VIEW IF EXISTS tempoo_analytics.hourly_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.browser_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.os_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.device_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.country_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.referrer_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.path_stats_mv;

-- 2. Clear the target tables (optional but recommended if they were empty/wrong)
TRUNCATE TABLE IF EXISTS tempoo_analytics.hourly_stats;
TRUNCATE TABLE IF EXISTS tempoo_analytics.dimension_stats;

-- 3. Create new, robust MVs
-- 3.1 Hourly
CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.hourly_stats_mv
TO tempoo_analytics.hourly_stats
AS SELECT
    site_id,
    toDate(timestamp) AS date,
    toHour(timestamp) AS hour,
    uniqState(user_hash) AS unique_visitors,
    count() AS total_events
FROM tempoo_analytics.events
GROUP BY site_id, date, hour;

-- 3.2 Browser
CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.browser_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'browser' as dimension_name, browser as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

-- 3.3 OS
CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.os_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'os' as dimension_name, os as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

-- 3.4 Device
CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.device_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'device' as dimension_name, device_type as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

-- 3.5 Country
CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.country_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'country' as dimension_name, country_code as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

-- 3.6 Referrer
CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.referrer_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'referrer' as dimension_name, referrer_domain as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' AND referrer_domain != '' GROUP BY site_id, date, dimension_name, dimension_value;

-- 3.7 Path
CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.path_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'path' as dimension_name, path as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

-- 4. Populate historical data (backfill)
-- 4.1 Hourly
INSERT INTO tempoo_analytics.hourly_stats 
SELECT site_id, toDate(timestamp) as date, toHour(timestamp) as hour, uniqState(user_hash), count() 
FROM tempoo_analytics.events GROUP BY site_id, date, hour;

-- 4.2 Dimensions (we insert all the unified dimensions)
INSERT INTO tempoo_analytics.dimension_stats
-- Browser
SELECT site_id, toDate(timestamp) as date, 'browser', browser, uniqState(user_hash), count() 
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, browser
UNION ALL
-- OS
SELECT site_id, toDate(timestamp) as date, 'os', os, uniqState(user_hash), count() 
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, os
UNION ALL
-- Device
SELECT site_id, toDate(timestamp) as date, 'device', device_type, uniqState(user_hash), count() 
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, device_type
UNION ALL
-- Country
SELECT site_id, toDate(timestamp) as date, 'country', country_code, uniqState(user_hash), count() 
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, country_code
UNION ALL
-- Referrer
SELECT site_id, toDate(timestamp) as date, 'referrer', referrer_domain, uniqState(user_hash), count() 
FROM tempoo_analytics.events WHERE event_name = 'pageview' AND referrer_domain != '' GROUP BY site_id, date, referrer_domain
UNION ALL
-- Path
SELECT site_id, toDate(timestamp) as date, 'path', path, uniqState(user_hash), count() 
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, path;
