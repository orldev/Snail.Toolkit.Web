#!/bin/sh
set -eu

# TZ влияет на то, что читает браузер, но /etc/localtime и /etc/timezone от неё
# не меняются, и диагностика по файлу врала бы. Приводим систему в соответствие,
# а показываем то, что увидит страница.
if [ -n "${TZ:-}" ] && [ -f "/usr/share/zoneinfo/${TZ}" ]; then
  ln -snf "/usr/share/zoneinfo/${TZ}" /etc/localtime
  echo "${TZ}" > /etc/timezone
fi

echo "timezone   $(node -p 'Intl.DateTimeFormat().resolvedOptions().timeZone')"
echo "screen     ${SCREEN}"
echo "firefox    $(/opt/firefox/firefox --version 2>/dev/null || echo unavailable)"
echo "patches    $(grep -c SNAIL /opt/firefox/playwright.cfg) in playwright.cfg"

# Xvfb поднимается явно, а не через xvfb-run. Проверено: в роли PID 1 тот
# запускает X-сервер и до самой команды не доходит — контейнер остаётся живым, а
# сервера внутри нет, и наружу это выглядит как «socket hang up» у клиента.
Xvfb "${DISPLAY}" -screen 0 "${SCREEN}" -nolisten tcp &

until [ -e "/tmp/.X11-unix/X${DISPLAY#:}" ]; do
  sleep 0.2
done

echo "display    ${DISPLAY} ready"

exec node /app/server.mjs
