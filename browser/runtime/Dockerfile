# Рантайм: браузерный сервер, к которому подключается тулкит.
#
# Отдельный образ от сборочного намеренно. Сборочный весит 12 ГБ — это дерево
# исходников Firefox и тулчейн, которым в рантайме делать нечего. Здесь лежит
# только распакованный браузер, playwright-core и виртуальный дисплей.
#
# Собирается из каталога browser/, чтобы дотянуться до out/ и до правленых
# настроек:
#   docker build -f runtime/Dockerfile -t ff-juggler:155.0 .

# Архив распаковывается в отдельной ступени: так 82 МБ сжатого архива не
# остаются лишним слоем в готовом образе.
FROM debian:bookworm-slim AS unpack

RUN apt-get update && apt-get install -y --no-install-recommends xz-utils \
 && rm -rf /var/lib/apt/lists/*

COPY out/firefox-*.tar.xz /tmp/
RUN tar -xf /tmp/firefox-*.tar.xz -C /opt

FROM node:22-bookworm-slim

ARG PLAYWRIGHT=1.63.0

# Пояс и разрешение экрана — не косметика, а условие работы. Замер 13.09.2026:
# с поясом UTC Google отвечает /sorry/ даже нашей сборке, а 1280x1024 (умолчание
# xvfb) — разрешение, которого сегодня почти ни у кого нет. Шрифты по той же
# причине: голый контейнер знает три штуки из двенадцати обычных.
ENV TZ=America/New_York \
    SCREEN=1920x1080x24 \
    PORT=3000

RUN apt-get update && apt-get install -y --no-install-recommends \
      xvfb tzdata fontconfig ca-certificates \
      fonts-liberation fonts-dejavu-core fonts-noto-core \
      libgtk-3-0 libdbus-glib-1-2 libxt6 libasound2 libx11-xcb1 \
      libxcomposite1 libxdamage1 libxfixes3 libxrandr2 libgbm1 \
      libpango-1.0-0 libcairo2 libatk1.0-0 libatk-bridge2.0-0 libcups2 \
 && fc-cache -f \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# playwright-core, а не playwright: нам не нужны его браузеры, свой уже есть.
# Разница — 311 МБ скачанных дистрибутивов, которые никто не откроет.
RUN npm init -y >/dev/null \
 && npm i --no-audit --no-fund playwright-core@${PLAYWRIGHT} \
 && npm cache clean --force

COPY --from=unpack /opt/firefox /opt/firefox

# Настройки Playwright кладёт в дистрибутив уже после ./mach package, своим
# build.sh; наш сборочный Dockerfile этого не делает. Здесь же лежат семь наших
# правок — что каждая снимает, описано в PATCHES.md.
COPY preferences/playwright.cfg /opt/firefox/
COPY preferences/00-playwright-prefs.js /opt/firefox/defaults/pref/

COPY runtime/server.mjs /app/server.mjs
COPY runtime/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

EXPOSE 3000

ENTRYPOINT ["/app/entrypoint.sh"]
