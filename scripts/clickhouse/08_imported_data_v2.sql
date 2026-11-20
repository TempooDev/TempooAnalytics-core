-- Imported data v2 (OpenSpec change complete-plausible-import).
-- Columns for the complete Plausible totals and the job_id of the job that inserted them.
-- Idempotent: it can be applied twice in a row without error.

ALTER TABLE imported_daily_stats ADD COLUMN IF NOT EXISTS visits UInt64 DEFAULT 0;
ALTER TABLE imported_daily_stats ADD COLUMN IF NOT EXISTS bounces UInt64 DEFAULT 0;
ALTER TABLE imported_daily_stats ADD COLUMN IF NOT EXISTS visit_duration UInt64 DEFAULT 0;
ALTER TABLE imported_daily_stats ADD COLUMN IF NOT EXISTS job_id UUID;

ALTER TABLE imported_dimension_stats ADD COLUMN IF NOT EXISTS visits UInt64 DEFAULT 0;
ALTER TABLE imported_dimension_stats ADD COLUMN IF NOT EXISTS job_id UUID;

-- source now takes fixed values: 'plausible' instead of 'plausible-import'.
-- source is part of the sorting key and ClickHouse does not allow ALTER ... UPDATE on
-- key columns, so the rows are copied with the new value and the old ones are deleted.
INSERT INTO imported_daily_stats (site_id, date, visitors, pageviews, source)
SELECT site_id, date, visitors, pageviews, 'plausible'
FROM imported_daily_stats
WHERE source = 'plausible-import';

ALTER TABLE imported_daily_stats DELETE WHERE source = 'plausible-import' SETTINGS mutations_sync = 2;

INSERT INTO imported_dimension_stats (site_id, date, dimension_name, dimension_value, visitors, pageviews, source)
SELECT site_id, date, dimension_name, dimension_value, visitors, pageviews, 'plausible'
FROM imported_dimension_stats
WHERE source = 'plausible-import';

ALTER TABLE imported_dimension_stats DELETE WHERE source = 'plausible-import' SETTINGS mutations_sync = 2;
