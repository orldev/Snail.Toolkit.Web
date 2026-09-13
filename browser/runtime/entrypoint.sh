#!/bin/sh
set -eu

echo "timezone   $(cat /etc/timezone 2>/dev/null || echo unknown)"
echo "screen     ${SCREEN}"
echo "firefox    $(/opt/firefox/firefox --version 2>/dev/null || echo unavailable)"
echo "patches    $(grep -c SNAIL /opt/firefox/playwright.cfg) in playwright.cfg"

exec xvfb-run -a -s "-screen 0 ${SCREEN}" node /app/server.mjs
