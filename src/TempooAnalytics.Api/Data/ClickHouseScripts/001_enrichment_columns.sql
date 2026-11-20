-- Migration v001: Add Enrichment Columns
ALTER TABLE tempoo_analytics.events ADD COLUMN IF NOT EXISTS page_title String AFTER path;
ALTER TABLE tempoo_analytics.events ADD COLUMN IF NOT EXISTS bot_name LowCardinality(String) DEFAULT '' AFTER traffic_type;
ALTER TABLE tempoo_analytics.events ADD COLUMN IF NOT EXISTS company_name String DEFAULT '' AFTER bot_name;
ALTER TABLE tempoo_analytics.events ADD COLUMN IF NOT EXISTS company_domain String DEFAULT '' AFTER company_name;
