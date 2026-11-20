#!/usr/bin/env bash
# Uploads or downloads the published tracker files (each script with its .gz and .br) to or from one
# folder of the R2 bucket. analytics.js loads heatmaps.js and heatmap-overlay.js from its own folder,
# so the three always travel together.
#
#   r2-scripts.sh put <bucket-folder> <local-dir> [cache-control]
#   r2-scripts.sh get <bucket-folder> <local-dir>
#
# An empty <bucket-folder> ("") means the bucket root, where the CDN serves production.
#
# Needs R2_BUCKET_NAME, CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID, and wrangler on the PATH.
set -euo pipefail

action=$1
folder=$2
dir=$3
cache=${4:-}
names=(analytics heatmaps heatmap-overlay)

mkdir -p "$dir"
for name in "${names[@]}"; do
  for ext in js js.gz js.br; do
    key="$R2_BUCKET_NAME/${folder:+$folder/}$name.$ext"
    file="$dir/$name.$ext"
    if [ "$action" = get ]; then
      wrangler r2 object get "$key" --file="$file" --remote
      continue
    fi
    args=(--file="$file" --content-type="application/javascript" --remote)
    case $ext in
      js.gz) args+=(--content-encoding="gzip") ;;
      js.br) args+=(--content-encoding="br") ;;
    esac
    [ -n "$cache" ] && args+=(--cache-control="$cache")
    wrangler r2 object put "$key" "${args[@]}"
  done
done
