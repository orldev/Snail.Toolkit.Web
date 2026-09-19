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

screen_number="${DISPLAY#:}"

# ЗАМОК ОТ ПРОШЛОЙ ЖИЗНИ. Когда внутри умирает Firefox, с ним уходит и сервер, а
# /tmp/.X99-lock остаётся — и записываемый слой переживает docker restart, так что
# следующий старт падает с «Server is already active for display 99», затем
# «cannot open display: :99». Снаружи это выглядит как контейнер в цикле
# перезапуска без единой понятной строки, и лечилось только пересозданием.
# Замок принадлежит процессу, которого уже нет: мы здесь одни, PID 1, и если файл
# на месте — он мусор.
if [ -e "/tmp/.X${screen_number}-lock" ] || [ -e "/tmp/.X11-unix/X${screen_number}" ]; then
  echo "display    stale lock for ${DISPLAY} left by a previous run, removing"
  rm -f "/tmp/.X${screen_number}-lock" "/tmp/.X11-unix/X${screen_number}"
fi

# Xvfb поднимается явно, а не через xvfb-run. Проверено: в роли PID 1 тот
# запускает X-сервер и до самой команды не доходит — контейнер остаётся живым, а
# сервера внутри нет, и наружу это выглядит как «socket hang up» у клиента.
Xvfb "${DISPLAY}" -screen 0 "${SCREEN}" -nolisten tcp &
display_pid=$!

# Ждём сокет, но не вечно и не вслепую: прежний цикл крутился, пока файл не
# появится, поэтому упавший Xvfb давал вечно живой контейнер, который никому не
# отвечает. Теперь смерть X-сервера и его молчание — две разные строки, и обе
# заканчивают запуск с ненулевым кодом, чтобы перезапуск решал тот, кто им
# управляет.
waited=0
until [ -e "/tmp/.X11-unix/X${screen_number}" ]; do
  if ! kill -0 "${display_pid}" 2>/dev/null; then
    echo "display    Xvfb died before ${DISPLAY} was ready" >&2
    exit 1
  fi

  if [ "${waited}" -ge 100 ]; then
    echo "display    ${DISPLAY} did not come up within 20 seconds" >&2
    kill "${display_pid}" 2>/dev/null || true
    exit 1
  fi

  waited=$((waited + 1))
  sleep 0.2
done

echo "display    ${DISPLAY} ready"

exec node /app/server.mjs
