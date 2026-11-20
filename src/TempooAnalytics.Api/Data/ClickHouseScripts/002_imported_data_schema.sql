-- Copy of scripts/clickhouse/07_imported_data_schema.sql for production: the Docker image only includes this folder.
-- Schema for imported analytics data (GA4, CSV, etc.)
-- This data already arrives aggregated, so we do not use AggregateFunction(uniq, ...)
-- but plain sums instead.

CREATE TABLE IF NOT EXISTS tempoo_analytics.imported_daily_stats (
    site_id UUID,
    date Date,
    visitors UInt64,
    pageviews UInt64,
    source String DEFAULT 'import'
) ENGINE = SummingMergeTree()
ORDER BY (site_id, date, source);

CREATE TABLE IF NOT EXISTS tempoo_analytics.imported_dimension_stats (
    site_id UUID,
    date Date,
    dimension_name LowCardinality(String),
    dimension_value String,
    visitors UInt64,
    pageviews UInt64,
    source String DEFAULT 'import'
) ENGINE = SummingMergeTree()
ORDER BY (site_id, date, dimension_name, dimension_value, source);
