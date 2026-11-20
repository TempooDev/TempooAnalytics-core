USE tempoo_analytics;

-- ASN dictionary (Internet providers)
-- Maps the ASN number (UInt32) to the organisation name.
CREATE DICTIONARY IF NOT EXISTS tempoo_analytics.asn_dict (
    autonomous_system_number UInt32,
    autonomous_system_organization String
)
PRIMARY KEY autonomous_system_number
SOURCE(FILE(PATH '/var/lib/clickhouse/user_files/GeoLite2-ASN-Blocks-IPv4.csv' FORMAT 'CSVWithNames'))
LIFETIME(MIN 86400 MAX 86400)
LAYOUT(HASHED());

-- Country dictionary
-- Maps the ISO code (e.g. 'ES') to the friendly name (e.g. 'Spain').
CREATE DICTIONARY IF NOT EXISTS tempoo_analytics.country_dict (
    country_iso_code String,
    country_name String
)
PRIMARY KEY country_iso_code
SOURCE(FILE(PATH '/var/lib/clickhouse/user_files/GeoLite2-Country-Locations-en.csv' FORMAT 'CSVWithNames'))
LIFETIME(MIN 86400 MAX 86400)
LAYOUT(COMPLEX_KEY_HASHED());
