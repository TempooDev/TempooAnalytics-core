USE tempoo_analytics;

ALTER TABLE events ADD COLUMN IF NOT EXISTS asn UInt32 AFTER traffic_type;
ALTER TABLE events ADD COLUMN IF NOT EXISTS data String AFTER user_agent;

CREATE DICTIONARY IF NOT EXISTS asn_dict (
    autonomous_system_number UInt32,
    autonomous_system_organization String
)
PRIMARY KEY autonomous_system_number
SOURCE(FILE(PATH '/var/lib/clickhouse/user_files/GeoLite2-ASN-Blocks-IPv4.csv' FORMAT 'CSVWithNames'))
LIFETIME(MIN 86400 MAX 86400)
LAYOUT(HASHED());

CREATE DICTIONARY IF NOT EXISTS country_dict (
    country_iso_code String,
    country_name String
)
PRIMARY KEY country_iso_code
SOURCE(FILE(PATH '/var/lib/clickhouse/user_files/GeoLite2-Country-Locations-en.csv' FORMAT 'CSVWithNames'))
LIFETIME(MIN 86400 MAX 86400)
LAYOUT(COMPLEX_KEY_HASHED());
