#!/bin/sh
# Writes /usr/share/nginx/html/config.js (runtime settings of the web app) from SUPPORT_EMAIL and BRAND_NAME.
# Quotes, backslashes, angle brackets and control characters are dropped so a value can never break out of the string.
clean() { printf '%s' "$1" | tr -d '"\\<>\r\n\t'; }
printf 'window.__TEMPOO_CONFIG__ = { supportEmail: "%s", brandName: "%s" };\n' "$(clean "$SUPPORT_EMAIL")" "$(clean "$BRAND_NAME")" > /usr/share/nginx/html/config.js
