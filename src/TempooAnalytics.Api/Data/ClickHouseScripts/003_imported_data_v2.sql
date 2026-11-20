-- Copy of scripts/clickhouse/08_imported_data_v2.sql for production: the Docker image only includes this folder.
-- Imported data v2 (OpenSpec change complete-plausible-import).
-- Columns for the complete Plausible totals and the job_id of the job that inserted them.
-- Idempotent: it can be applied twice in a row without error.

ALTER TABLE tempoo_analytics.imported_daily_stats ADD COLUMN IF NOT EXISTS visits UInt64 DEFAULT 0;
ALTER TABLE tempoo_analytics.imported_daily_stats ADD COLUMN IF NOT EXISTS bounces UInt64 DEFAULT 0;
ALTER TABLE tempoo_analytics.imported_daily_stats ADD COLUMN IF NOT EXISTS visit_duration UInt64 DEFAULT 0;
ALTER TABLE tempoo_analytics.imported_daily_stats ADD COLUMN IF NOT EXISTS job_id UUID;

ALTER TABLE tempoo_analytics.imported_dimension_stats ADD COLUMN IF NOT EXISTS visits UInt64 DEFAULT 0;
ALTER TABLE tempoo_analytics.imported_dimension_stats ADD COLUMN IF NOT EXISTS job_id UUID;

-- source now takes fixed values: 'plausible' instead of 'plausible-import'.
-- source is part of the sorting key and ClickHouse does not allow ALTER ... UPDATE on
-- key columns, so the rows are copied with the new value and the old ones are deleted.
INSERT INTO tempoo_analytics.imported_daily_stats (site_id, date, visitors, pageviews, source)
SELECT site_id, date, visitors, pageviews, 'plausible'
FROM tempoo_analytics.imported_daily_stats
WHERE source = 'plausible-import';

ALTER TABLE tempoo_analytics.imported_daily_stats DELETE WHERE source = 'plausible-import' SETTINGS mutations_sync = 2;

INSERT INTO tempoo_analytics.imported_dimension_stats (site_id, date, dimension_name, dimension_value, visitors, pageviews, source)
SELECT site_id, date, dimension_name, dimension_value, visitors, pageviews, 'plausible'
FROM tempoo_analytics.imported_dimension_stats
WHERE source = 'plausible-import';

ALTER TABLE tempoo_analytics.imported_dimension_stats DELETE WHERE source = 'plausible-import' SETTINGS mutations_sync = 2;
