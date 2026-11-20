# GeoLite2: download on startup

The MaxMind GeoLite2 databases cannot be redistributed with the code. Each service looks for them on disk and, if they are missing or older than 7 days and credentials are available, downloads them from MaxMind's official service.

| Variable | Purpose |
|---|---|
| `MAXMIND_ACCOUNT_ID`, `MAXMIND_LICENSE_KEY` | MaxMind credentials (free account). Without them nothing is downloaded. |
| `GEOIP_DATA_DIR` | API only: folder of `GeoLite2-Country.mmdb` (`Data/` by default). |
| `GEOIP_CITY_DB_PATH`, `GEOIP_ASN_DB_PATH` | Go only: paths of the `.mmdb` files (current defaults). |

- **API (C#):** `GeoIpDatabaseProvisioner` loads the file if it exists, downloads it if it is missing or stale, and checks every 24 h; the reader is swapped live (`GeoIpDatabase`). With neither a file nor credentials the API starts and `GetCountryCode` returns `null` (it used to throw `FileNotFoundException`).
- **Go (ingestion):** `internal/geoip` downloads on startup if the file is missing or stale; refreshing an already loaded database requires restarting the service.
- If the download fails, the existing file is kept.
- When the `.mmdb` files exist and are up to date, behaviour is unchanged.

## ClickHouse: dictionary CSVs

`02_init_dict.sql` creates `asn_dict` (reads `GeoLite2-ASN-Blocks-IPv4.csv`) and `country_dict` (reads `GeoLite2-Country-Locations-en.csv`) from ClickHouse's `user_files`. Those CSVs are not versioned either: `deploy/clickhouse/geoip/provision-csv.sh` provisions them with the same variables (`MAXMIND_ACCOUNT_ID`, `MAXMIND_LICENSE_KEY`).

| Variable | Purpose |
|---|---|
| `GEOIP_CSV_DIR` | Destination, the ClickHouse `user_files` folder (in the repo: `deploy/clickhouse/user_files`). |
| `GEOIP_MAX_AGE_DAYS` | Age after which the files are downloaded again (7). |
| `GEOIP_DOWNLOAD_BASE` | Tests only: replaces `https://download.maxmind.com/geoip/databases`. |

The script downloads `GeoLite2-ASN-CSV` and `GeoLite2-Country-CSV` (zip), extracts the `.csv` files and renames them into place (ClickHouse never reads a half-written file). The credentials are passed to `curl` through stdin, not as arguments, and are not written anywhere. It needs `curl` and `unzip`. It never fails: without credentials, or if the download fails (401 wrong key, 429 daily quota), it keeps whatever is there and, if there is nothing, writes header-only CSVs, so the dictionaries load empty (no country or provider names) and ClickHouse still starts. The dictionary reloads the file only when it changes (`LIFETIME` 24 h).

Where it runs:

- **Aspire (local):** the AppHost starts a `clickhouse-geoip-init` container (alpine) before ClickHouse, which runs the script against `deploy/clickhouse/user_files`. Optional credentials in user secrets: `Parameters:MaxMindAccountId` and `Parameters:MaxMindLicenseKey`.
- **Your own ClickHouse on the host:** run the script with `GEOIP_CSV_DIR` pointing at the real `user_files` and the credentials exported, by hand or from a weekly cron job.

## Removal of the files from the tree

The `.mmdb` files (API and Go) and the 20 CSVs in `deploy/clickhouse/user_files` are no longer versioned (`.gitignore` excludes them; only `.gitkeep` remains). Before deploying anything that contains this change, define `MAXMIND_ACCOUNT_ID` and `MAXMIND_LICENSE_KEY` in the environment and run the script against the real ClickHouse; without keys, the API will have no country-by-IP until they are defined.
