#!/usr/bin/env bash
# Regenerates the admin console stylesheets (src/TempooAnalytics.Api/Infrastructure/Admin/Assets/*.css) after a change to the
# classes or icons of a page under src/TempooAnalytics.Api/Pages (or a cloud page rendered inside one). Needs node and npm.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
tools="${ADMIN_ASSETS_TOOLS:-}"
if [ -z "$tools" ]; then
  tools="$(mktemp -d)"
  trap 'rm -rf "$tools"' EXIT
fi
if [ ! -x "$tools/node_modules/.bin/tailwindcss" ]; then
  (cd "$tools" && npm init -y >/dev/null && npm install --no-audit --no-fund --silent \
    tailwindcss@3.4.17 primeicons@7.0.0 @fontsource-variable/plus-jakarta-sans@5.3.0)
fi
"$tools/node_modules/.bin/tailwindcss" -c "$here/tailwind.config.cjs" -i "$here/input.css" -o "$tools/tailwind.css" --minify
ADMIN_ASSETS_TOOLS="$tools" node "$here/build-admin-css.mjs" "$tools/tailwind.css"
