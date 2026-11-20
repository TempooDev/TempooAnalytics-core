-- Copy of scripts/clickhouse/10_events_insert_dedup.sql for production: the Docker image only includes this folder.
-- Idempotent ingestion (block 4 of the ingestion optimization): ingestion retries a failed batch INSERT
-- with the same insert_deduplication_token. On non-replicated MergeTree tables, ClickHouse only
-- drops the repeated block if non_replicated_deduplication_window > 0 (it keeps the hashes of the last
-- N blocks). It is also needed on the target tables of the events materialized views: without it,
-- events drops the retry but hourly_stats and dimension_stats count it twice (verified with
-- ClickHouse 26.10). The views' blocks are identified by the source block, so two different inserts
-- with the same aggregated result are not mistaken for each other.
-- Idempotent: it can be applied twice in a row without error.

ALTER TABLE tempoo_analytics.events MODIFY SETTING non_replicated_deduplication_window = 1000;
ALTER TABLE tempoo_analytics.hourly_stats MODIFY SETTING non_replicated_deduplication_window = 1000;
ALTER TABLE tempoo_analytics.dimension_stats MODIFY SETTING non_replicated_deduplication_window = 1000;
