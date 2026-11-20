#!/bin/sh
# Provisions the GeoLite2 CSVs that the ClickHouse dictionaries read (02_init_dict.sql). Same contract as the
# .mmdb provisioning of the API and the Go worker: the MaxMind licence forbids shipping the files, so they are
# downloaded with the operator's own free account.
#
#   MAXMIND_ACCOUNT_ID, MAXMIND_LICENSE_KEY   credentials (never logged, never written to disk)
#   GEOIP_CSV_DIR        destination, the ClickHouse user_files folder (default: /geoip/user_files)
#   GEOIP_MAX_AGE_DAYS   refresh after this many days (default 7, the MaxMind recommendation)
#   GEOIP_DOWNLOAD_BASE  default https://download.maxmind.com/geoip/databases (override only for tests)
#
# Never fails the caller: without credentials, or when a download fails, the existing files are kept and, if
# there are none, header-only CSVs are written so the dictionaries load empty (no country/ASN names) instead of
# erroring. Needs curl and unzip. Safe to run from cron or as a one-shot container.
set -u

DIR="${GEOIP_CSV_DIR:-/geoip/user_files}"
MAX_AGE_DAYS="${GEOIP_MAX_AGE_DAYS:-7}"
BASE="${GEOIP_DOWNLOAD_BASE:-https://download.maxmind.com/geoip/databases}"
EDITIONS="GeoLite2-ASN-CSV GeoLite2-Country-CSV"
ASN_FILE="GeoLite2-ASN-Blocks-IPv4.csv"
COUNTRY_FILE="GeoLite2-Country-Locations-en.csv"

log() { echo "[geoip-csv] $*"; }

mkdir -p "$DIR" || { log "cannot create $DIR; geo dictionaries stay empty"; exit 0; }

# Header-only placeholders (same columns as the MaxMind files) so CSVWithNames dictionaries load as empty.
write_placeholders() {
    [ -f "$DIR/$ASN_FILE" ] || printf 'network,autonomous_system_number,autonomous_system_organization\n' > "$DIR/$ASN_FILE"
    [ -f "$DIR/$COUNTRY_FILE" ] || printf 'geoname_id,locale_code,continent_code,continent_name,country_iso_code,country_name,is_in_european_union\n' > "$DIR/$COUNTRY_FILE"
}

# Real file = more than a header line.
is_real() { [ -f "$1" ] && [ "$(wc -l < "$1")" -gt 1 ]; }

# fresh <file>: real and younger than MAX_AGE_DAYS.
fresh() { is_real "$1" && [ -z "$(find "$1" -mtime +"$MAX_AGE_DAYS" 2>/dev/null)" ]; }

if fresh "$DIR/$ASN_FILE" && fresh "$DIR/$COUNTRY_FILE"; then
    log "GeoLite2 CSVs are up to date in $DIR"
    exit 0
fi

if [ -z "${MAXMIND_ACCOUNT_ID:-}" ] || [ -z "${MAXMIND_LICENSE_KEY:-}" ]; then
    write_placeholders
    log "MAXMIND_ACCOUNT_ID / MAXMIND_LICENSE_KEY not set: ClickHouse geo dictionaries (ASN, country names) stay empty. Set both to download them."
    exit 0
fi

TMP="$(mktemp -d "${TMPDIR:-/tmp}/geoip-csv.XXXXXX")" || { write_placeholders; exit 0; }
trap 'rm -rf "$TMP"' EXIT
ok=1

for edition in $EDITIONS; do
    zip="$TMP/$edition.zip"
    # Credentials go through stdin (--config -), not argv, so they never show in `ps`.
    code="$(printf 'user = "%s:%s"\n' "$MAXMIND_ACCOUNT_ID" "$MAXMIND_LICENSE_KEY" |
        curl -sS --config - -L --max-time 300 -o "$zip" -w '%{http_code}' \
            "$BASE/$edition/download?suffix=zip" 2>/dev/null)" || code="000"
    if [ "$code" != "200" ]; then
        log "download of $edition failed with HTTP $code (401 = bad credentials, 429 = daily quota); keeping existing files"
        ok=0; continue
    fi
    if ! { mkdir -p "$TMP/$edition" && unzip -q -j -o "$zip" '*.csv' -d "$TMP/$edition" 2>/dev/null; }; then
        log "$edition archive could not be unpacked"
        ok=0
    fi
done

if [ "$ok" = "1" ] && is_real "$TMP/GeoLite2-ASN-CSV/$ASN_FILE" && is_real "$TMP/GeoLite2-Country-CSV/$COUNTRY_FILE"; then
    for f in "$TMP"/GeoLite2-ASN-CSV/*.csv "$TMP"/GeoLite2-Country-CSV/*.csv; do
        # Same folder, then rename: ClickHouse never reads a half-written file.
        cp "$f" "$DIR/.$(basename "$f").part" && mv -f "$DIR/.$(basename "$f").part" "$DIR/$(basename "$f")"
    done
    log "GeoLite2 CSVs downloaded to $DIR"
else
    write_placeholders
    log "GeoLite2 CSVs not updated; keeping what is in $DIR"
fi
exit 0
