-- ============================================================================
-- CLICKHOUSE MIGRATION SCRIPT (PRODUCTION)
-- This script unifies the table structure, dictionaries and MVs.
-- ============================================================================

USE tempoo_analytics;

-- 1. ENSURE THE STRUCTURE OF THE 'events' TABLE
-- We add the 'asn' and 'data' columns if they do not exist (seen in a capture, just in case).
ALTER TABLE tempoo_analytics.events ADD COLUMN IF NOT EXISTS asn UInt32 AFTER traffic_type;
ALTER TABLE tempoo_analytics.events ADD COLUMN IF NOT EXISTS data String AFTER user_agent;

-- 2. DICCIONARIOS (GeoLite2)
-- Make sure the CSV files are in /var/lib/clickhouse/user_files/ on the server.

CREATE DICTIONARY IF NOT EXISTS tempoo_analytics.asn_dict (
    autonomous_system_number UInt32,
    autonomous_system_organization String
)
PRIMARY KEY autonomous_system_number
SOURCE(FILE(PATH '/var/lib/clickhouse/user_files/GeoLite2-ASN-Blocks-IPv4.csv' FORMAT 'CSVWithNames'))
LIFETIME(MIN 86400 MAX 86400)
LAYOUT(HASHED());

CREATE DICTIONARY IF NOT EXISTS tempoo_analytics.country_dict (
    country_iso_code String,
    country_name String
)
PRIMARY KEY country_iso_code
SOURCE(FILE(PATH '/var/lib/clickhouse/user_files/GeoLite2-Country-Locations-en.csv' FORMAT 'CSVWithNames'))
LIFETIME(MIN 86400 MAX 86400)
LAYOUT(COMPLEX_KEY_HASHED());


-- 3. RE-CREATION OF MATERIALIZED VIEWS AND AGGREGATION TABLES
-- If this fails, it is because of locks. Try dropping them in this exact order.

-- 3.1 Drop views (dependencies first)
DROP VIEW IF EXISTS tempoo_analytics.hourly_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.browser_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.os_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.device_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.country_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.referrer_stats_mv;
DROP VIEW IF EXISTS tempoo_analytics.path_stats_mv;

-- 3.2 Drop tables (IMPORTANT: no IF NOT EXISTS, to force re-creation if the DROP fails)
DROP TABLE IF EXISTS tempoo_analytics.hourly_stats;
DROP TABLE IF EXISTS tempoo_analytics.dimension_stats;

-- 3.3 Direct re-creation (no IF NOT EXISTS, to ensure the new schema)
CREATE TABLE tempoo_analytics.hourly_stats (
    site_id UUID,
    date Date,
    hour UInt8,
    unique_visitors AggregateFunction(uniq, FixedString(64)),
    total_events SimpleAggregateFunction(sum, UInt64)
) ENGINE = AggregatingMergeTree()
ORDER BY (site_id, date, hour);

CREATE TABLE tempoo_analytics.dimension_stats (
    site_id UUID,
    date Date,
    dimension_name LowCardinality(String),
    dimension_value String,
    unique_visitors AggregateFunction(uniq, FixedString(64)),
    pageviews SimpleAggregateFunction(sum, UInt64)
) ENGINE = AggregatingMergeTree()
ORDER BY (site_id, date, dimension_name, dimension_value);

-- 3.4 MV creation
CREATE MATERIALIZED VIEW tempoo_analytics.hourly_stats_mv TO tempoo_analytics.hourly_stats
AS SELECT site_id, toDate(timestamp) AS date, toHour(timestamp) AS hour, uniqState(user_hash) AS unique_visitors, count() AS total_events
FROM tempoo_analytics.events GROUP BY site_id, date, hour;

CREATE MATERIALIZED VIEW tempoo_analytics.browser_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'browser' as dimension_name, browser as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW tempoo_analytics.os_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'os' as dimension_name, os as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW tempoo_analytics.device_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'device' as dimension_name, device_type as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW tempoo_analytics.country_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'country' as dimension_name, country_code as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW tempoo_analytics.referrer_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'referrer' as dimension_name, referrer_domain as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' AND referrer_domain != '' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW tempoo_analytics.path_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'path' as dimension_name, path as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;


-- 4. DATA POPULATION (BACKFILL)
INSERT INTO tempoo_analytics.hourly_stats 
SELECT site_id, toDate(timestamp) as date, toHour(timestamp) as hour, uniqState(user_hash), count() 
FROM tempoo_analytics.events GROUP BY site_id, date, hour;

INSERT INTO tempoo_analytics.dimension_stats
SELECT site_id, toDate(timestamp) as date, 'browser', browser, uniqState(user_hash), count() FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, browser
UNION ALL
SELECT site_id, toDate(timestamp) as date, 'os', os, uniqState(user_hash), count() FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, os
UNION ALL
SELECT site_id, toDate(timestamp) as date, 'device', device_type, uniqState(user_hash), count() FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, device_type
UNION ALL
SELECT site_id, toDate(timestamp) as date, 'country', country_code, uniqState(user_hash), count() FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, country_code
UNION ALL
SELECT site_id, toDate(timestamp) as date, 'referrer', referrer_domain, uniqState(user_hash), count() FROM tempoo_analytics.events WHERE event_name = 'pageview' AND referrer_domain != '' GROUP BY site_id, date, referrer_domain
UNION ALL
SELECT site_id, toDate(timestamp) as date, 'path', path, uniqState(user_hash), count() FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, path;


-- 5. VERIFICATION (run this to see real results)
-- SELECT site_id, date, uniqMerge(unique_visitors), sum(total_events) FROM tempoo_analytics.hourly_stats GROUP BY site_id, date;
