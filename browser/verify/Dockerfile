# Проверка собранного Firefox. Отдельный образ, потому что сборочный весит
# десятки гигабайт деревом и тулчейном, а здесь нужен только node и Playwright.
#
# BASE задан сборочным образом не из любви к слоям, а потому что на машине
# сборки помощник учётных данных Docker Desktop не работает вне интерактивного
# сеанса: любой pull, даже анонимный, падает. Локальный образ реестра не
# требует. На машине со здоровым Docker сюда подойдёт и ubuntu:24.04.
ARG BASE=ff-juggler-nowd:latest
FROM ${BASE}

# Версия Playwright должна совпадать с той, из чьих browser_patches взяты juggler
# и bootstrap.diff. Расхождение не согласовывается ничем: 428 Precondition
# Required сравнивает клиента с сервером, а не с juggler внутри браузера, поэтому
# оно молчит до первой тяжёлой страницы и валит браузер целиком — замер
# 20.09.2026 в PATCHES.md, раздел 7. Здесь 1.62.0, потому что патчи взяты с тега
# v1.62.0; двигать эту строку только вместе с FIREFOX_REV и patches/.
ARG PLAYWRIGHT=1.62.0
ARG NODE=v22.11.0

ENV DEBIAN_FRONTEND=noninteractive

# Шрифты и пояс здесь не для красоты. Голый контейнер знает три шрифта из
# двенадцати обычных и живёт по UTC; и то и другое читается со страницы одной
# строкой, а для адреса из США пояс UTC ещё и противоречит адресу. Это свойства
# СРЕДЫ, и никакой правкой playwright.cfg они не лечатся.
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates curl xz-utils xvfb tzdata fontconfig \
      fonts-liberation fonts-dejavu-core fonts-noto-core \
 && fc-cache -f \
 && rm -rf /var/lib/apt/lists/*

# Пояс должен совпадать с тем, на что указывает адрес выхода. Здесь стоит пояс
# самой машины сборки (Windows сообщает Eastern Standard Time), и он же
# согласуется с её американским адресом. Меняется через -e TZ=...
ENV TZ=America/New_York

# Node ставится с nodejs.org, а не из apt: в Ubuntu 24.04 лежит 18-я ветка, и
# зависеть от неё в проверке протокола не хочется.
# Архитектура берётся у самого образа, а не зашивается: сборка идёт и на amd64
# (машина сборки), и на arm64 (мак), и зашитый x64 сломал бы вторую молча.
RUN arch="$(dpkg --print-architecture)" \
 && case "$arch" in \
      amd64) node_arch=x64 ;; \
      arm64) node_arch=arm64 ;; \
      *) echo "unsupported architecture: $arch" >&2; exit 1 ;; \
    esac \
 && curl -fsSL "https://nodejs.org/dist/${NODE}/node-${NODE}-linux-${node_arch}.tar.xz" \
      | tar -xJ -C /usr/local --strip-components=1 \
 && node --version && npm --version

WORKDIR /verify

# Штатный Firefox от Playwright ставится рядом намеренно: он и есть контроль,
# с которым сравнивается наша сборка. --with-deps заодно тянет системные
# библиотеки, нужные обеим.
RUN npm init -y >/dev/null \
 && npm i --no-audit --no-fund playwright@${PLAYWRIGHT} \
 && npx playwright install --with-deps firefox

COPY verify/check.mjs /verify/check.mjs
COPY preferences/ /prefs/

# Распаковка вынесена в запуск: архив приезжает томом, в образе его нет.
# Два файла настроек Playwright кладёт в дистрибутив уже после ./mach package,
# своим build.sh; наш сборочный Dockerfile этого не делал, поэтому кладём здесь.
# Без playwright.cfg juggler не получит обязательных для себя настроек.
# Распаковка вынесена в запуск: архив приезжает томом, в образе его нет.
# Два файла настроек Playwright кладёт в дистрибутив уже после ./mach package,
# своим build.sh; наш сборочный Dockerfile этого не делал, поэтому кладём здесь.
# Без playwright.cfg juggler не получит обязательных для себя настроек.
#
# Дерево распаковывается ОДИН раз и переживает запуски, если /browser — том:
# метка хранит размер и время архива, и пока они те же, xz не трогаем. Без тома
# всё работает как раньше, просто распаковка каждый раз.
#
# Время печатается по стадиям: гадать, где оно уходит, дороже, чем измерить.
RUN printf '%s\n' \
  '#!/bin/bash' \
  'set -euo pipefail' \
  'started=$SECONDS' \
  'mkdir -p /browser /profile' \
  'archive=$(ls /out/firefox-*.tar.* | head -1)' \
  'want=$(stat -c %s-%Y "$archive")' \
  'if [ "$(cat /browser/.unpacked 2>/dev/null || true)" = "$want" ]; then' \
  '  echo "распаковка   пропущена, дерево на месте"' \
  'else' \
  '  rm -rf /browser/firefox /browser/.unpacked' \
  '  tar -xf "$archive" -C /browser' \
  '  echo "$want" > /browser/.unpacked' \
  '  echo "распаковка   $((SECONDS - started)) с"' \
  'fi' \
  'cp /prefs/playwright.cfg /browser/firefox/' \
  'mkdir -p /browser/firefox/defaults/pref' \
  'cp /prefs/00-playwright-prefs.js /browser/firefox/defaults/pref/' \
  'echo "настройки    $(grep -c SNAIL /browser/firefox/playwright.cfg) наших правок в playwright.cfg"' \
  'if [ "${STOCKCFG:-0}" = "1" ]; then' \
  '  for dist in /root/.cache/ms-playwright/firefox-*/firefox; do' \
  '    cp /prefs/playwright.cfg "$dist"/; mkdir -p "$dist"/defaults/pref;' \
  '    cp /prefs/00-playwright-prefs.js "$dist"/defaults/pref/;' \
  '    echo "настройки    положены И В ШТАТНЫЙ: $dist";' \
  '  done' \
  'fi' \
  'if [ "${FRESH:-0}" = "1" ]; then rm -rf /profile/* /profile/.[!.]* 2>/dev/null || true; echo "профиль      ОЧИЩЕН перед запуском"; fi' \
  'echo "профиль      $(du -sh /profile 2>/dev/null | cut -f1), файлов $(find /profile -type f 2>/dev/null | wc -l)"' \
  'started=$SECONDS' \
  'if [ "${HEADFUL:-0}" = "1" ]; then xvfb-run -a -s "-screen 0 ${SCREEN:-1920x1080x24}" node /verify/check.mjs; else node /verify/check.mjs; fi' \
  'echo "проверка     $((SECONDS - started)) с"' \
  > /verify/run.sh && chmod +x /verify/run.sh

ENTRYPOINT ["/verify/run.sh"]
