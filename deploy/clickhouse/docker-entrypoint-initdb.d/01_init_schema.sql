-- 1. Create the database
CREATE DATABASE IF NOT EXISTS tempoo_analytics;

-- 2. User creation is handled via environment variables (CLICKHOUSE_USER/PASSWORD)
-- But we need to ensure permissions are correct if the user is created by the container
-- The container creates the user with full access probably, let's just ensure we use the correct DB.

-- 3. Create the main events table
USE tempoo_analytics;

CREATE TABLE IF NOT EXISTS events (
    -- 1. Basic identifiers (the logical primary key)
    event_id UUID DEFAULT generateUUIDv4(),
    site_id UUID,
    user_hash FixedString(64),          -- Privacy: Hash(IP + UserAgent + Salt)
    visitor_id String,                  -- Visitor ID (persisted in a cookie)
    session_id UUID,                    -- Session ID
    
    -- 2. Time
    timestamp DateTime,
    date Date DEFAULT toDate(timestamp), -- Key for the monthly partitioning
    
    -- 3. Location
    country_code FixedString(2),        -- 2-letter ISO code (e.g. 'ES')
    city_name String,                   -- e.g. 'Granada'
    
    -- 4. Device and browser (optimised with LowCardinality)
    device_type LowCardinality(String), -- 'desktop', 'mobile', 'tablet'
    os LowCardinality(String),          -- 'Windows', 'iOS', 'Android'
    browser LowCardinality(String),     -- 'Chrome', 'Safari', 'Firefox'
    screen_width UInt16,                -- Screen width
    screen_height UInt16,               -- Screen height
    
    -- 5. Traffic and acquisition
    referrer String,                    -- Full origin URL
    referrer_domain String,             -- e.g. 'google.com' (for fast grouping)
    utm_source String,
    utm_medium String,
    utm_campaign String,
    
    -- 6. Behaviour (page visited / event fired)
    event_name LowCardinality(String),  -- 'pageview', 'click_pricing', etc.
    path String,                        -- e.g. '/pricing'
    
    -- 7. AI, bot and datacenter detection
    traffic_type Enum8(
        'human' = 1, 
        'bot_ia' = 2, 
        'crawler' = 3, 
        'vpn_proxy' = 4
    ) DEFAULT 'human',
    asn UInt32,                         -- Network ID (e.g. 15169 for Google)
    user_agent LowCardinality(String),  -- Kept in case future bots need to be re-analysed
    data String DEFAULT '{}'            -- Event metadata as JSON
) 
ENGINE = MergeTree()
PARTITION BY toYYYYMM(date)             -- Partitions the data by YYYYMM (e.g. 202602)
ORDER BY (site_id, date, event_name, user_hash) -- The primary index. It is IMPORTANT to keep this order
SETTINGS index_granularity = 8192;      -- Recommended standard setting

-- 4. Materialized Views for Dashboards

-- MV for Hourly Stats (Users/Events per hour)
CREATE TABLE IF NOT EXISTS tempoo_analytics.hourly_stats (
    site_id UUID,
    date Date,
    hour UInt8,
    unique_visitors AggregateFunction(uniq, FixedString(64)),
    total_events SimpleAggregateFunction(sum, UInt64)
) ENGINE = AggregatingMergeTree()
ORDER BY (site_id, date, hour);

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

-- MV for Dimension Stats (Referrers, Browsers, Devices, etc.)
CREATE TABLE IF NOT EXISTS tempoo_analytics.dimension_stats (
    site_id UUID,
    date Date,
    dimension_name LowCardinality(String), -- 'browser', 'os', 'device', 'country', 'referrer_domain'
    dimension_value String,
    unique_visitors AggregateFunction(uniq, FixedString(64)),
    pageviews SimpleAggregateFunction(sum, UInt64)
) ENGINE = AggregatingMergeTree()
ORDER BY (site_id, date, dimension_name, dimension_value);

-- We use separate MVs to populate the same dimension_stats table for efficiency
CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.browser_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'browser' as dimension_name, browser as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.os_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'os' as dimension_name, os as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.device_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'device' as dimension_name, device_type as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.country_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'country' as dimension_name, country_code as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.referrer_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'referrer' as dimension_name, referrer_domain as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' AND referrer_domain != '' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW IF NOT EXISTS tempoo_analytics.path_stats_mv TO tempoo_analytics.dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'path' as dimension_name, path as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM tempoo_analytics.events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

-- 5. User Permissions
-- None to grant here. 'api_worker' is defined in users.d/default-user.xml (the image's entrypoint writes it
-- from CLICKHOUSE_USER / CLICKHOUSE_PASSWORD), and users defined in configuration files already have every
-- privilege. GRANT on such a user fails ("Not enough privileges ... without grant option"), which aborted this
-- script on a fresh volume and skipped 02_init_dict.sql. In production the user is created with SQL and gets
-- its grants there (see README.md).
