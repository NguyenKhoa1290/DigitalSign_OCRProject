#!/bin/sh
set -eu

if [ -z "${PUBLIC_API_BASE_URL:-}" ]; then
    exit 0
fi

case "$PUBLIC_API_BASE_URL" in
    http://*|https://*) ;;
    *) echo 'PUBLIC_API_BASE_URL must start with http:// or https://' >&2; exit 1 ;;
esac

# Reject characters that could break the JSON file written into the static site.
case "$PUBLIC_API_BASE_URL" in
    *[!a-zA-Z0-9:/.%-]*) echo 'PUBLIC_API_BASE_URL contains unsupported characters' >&2; exit 1 ;;
esac

case "$PUBLIC_API_BASE_URL" in
    */) ;;
    *) echo 'PUBLIC_API_BASE_URL must end with /' >&2; exit 1 ;;
esac

printf '{"ApiBaseUrl":"%s"}\n' "$PUBLIC_API_BASE_URL" > /usr/share/nginx/html/appsettings.json
