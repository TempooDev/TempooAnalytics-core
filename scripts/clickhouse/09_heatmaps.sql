-- Heatmaps (OpenSpec change add-heatmaps).
-- Anonymous clicks: no user_hash, session_id, IP or country. 12-month retention for all plans.
-- Idempotent: it can be applied twice in a row without error.

CREATE TABLE IF NOT EXISTS heatmap_clicks (
    site_id UUID,
    date Date,
    path String,
    device_type LowCardinality(String),
    kind Enum8('click' = 1, 'rage' = 2, 'dead' = 3),
    x UInt16,
    y UInt32,
    viewport_width UInt16,
    selector String
) ENGINE = MergeTree()
PARTITION BY toYYYYMM(date)
ORDER BY (site_id, path, device_type, date)
TTL date + INTERVAL 12 MONTH;
