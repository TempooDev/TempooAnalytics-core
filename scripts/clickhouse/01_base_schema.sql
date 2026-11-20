CREATE TABLE IF NOT EXISTS events (
    event_id UUID DEFAULT generateUUIDv4(),
    site_id UUID,
    user_hash FixedString(64),
    visitor_id String,
    session_id UUID,
    timestamp DateTime,
    date Date DEFAULT toDate(timestamp),
    country_code FixedString(2),
    city_name String,
    device_type LowCardinality(String),
    os LowCardinality(String),
    browser LowCardinality(String),
    screen_width UInt16,
    screen_height UInt16,
    referrer String,
    referrer_domain String,
    utm_source String,
    utm_medium String,
    utm_campaign String,
    event_name LowCardinality(String),
    path String,
    traffic_type Enum8('human' = 1, 'bot_ia' = 2, 'crawler' = 3, 'vpn_proxy' = 4) DEFAULT 'human',
    user_agent LowCardinality(String)
) 
ENGINE = MergeTree()
PARTITION BY toYYYYMM(date)
ORDER BY (site_id, date, event_name, user_hash)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS hourly_stats (
    site_id UUID,
    date Date,
    hour UInt8,
    unique_visitors AggregateFunction(uniq, FixedString(64)),
    total_events SimpleAggregateFunction(sum, UInt64)
) ENGINE = AggregatingMergeTree()
ORDER BY (site_id, date, hour);

CREATE MATERIALIZED VIEW IF NOT EXISTS hourly_stats_mv
TO hourly_stats
AS SELECT
    site_id,
    toDate(timestamp) AS date,
    toHour(timestamp) AS hour,
    uniqState(user_hash) AS unique_visitors,
    count() AS total_events
FROM events
GROUP BY site_id, date, hour;

CREATE TABLE IF NOT EXISTS dimension_stats (
    site_id UUID,
    date Date,
    dimension_name LowCardinality(String),
    dimension_value String,
    unique_visitors AggregateFunction(uniq, FixedString(64)),
    pageviews SimpleAggregateFunction(sum, UInt64)
) ENGINE = AggregatingMergeTree()
ORDER BY (site_id, date, dimension_name, dimension_value);

CREATE MATERIALIZED VIEW IF NOT EXISTS browser_stats_mv TO dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'browser' as dimension_name, browser as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW IF NOT EXISTS os_stats_mv TO dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'os' as dimension_name, os as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW IF NOT EXISTS device_stats_mv TO dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'device' as dimension_name, device_type as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW IF NOT EXISTS country_stats_mv TO dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'country' as dimension_name, country_code as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW IF NOT EXISTS referrer_stats_mv TO dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'referrer' as dimension_name, referrer_domain as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM events WHERE event_name = 'pageview' AND referrer_domain != '' GROUP BY site_id, date, dimension_name, dimension_value;

CREATE MATERIALIZED VIEW IF NOT EXISTS path_stats_mv TO dimension_stats
AS SELECT site_id, toDate(timestamp) as date, 'path' as dimension_name, path as dimension_value, uniqState(user_hash) as unique_visitors, count() as pageviews
FROM events WHERE event_name = 'pageview' GROUP BY site_id, date, dimension_name, dimension_value;
