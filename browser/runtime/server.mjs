// Браузерный сервер: поднимает нашу сборку и ждёт подключений по ws.
// Тулкит ходит сюда через Web:Scraping:BrowserServer.

import { firefox } from 'playwright-core';

const port = Number(process.env.PORT ?? 3000);

// headless: false, а не true. Под xvfb это настоящее окно, и от него зависят
// системные цвета CSS: без дисплея Firefox отдаёт обобщённые подстановки,
// одинаковые у всех сборок Playwright в мире.
const server = await firefox.launchServer({
  executablePath: '/opt/firefox/firefox',
  headless: false,
  port,
  wsPath: '/',
  host: '0.0.0.0',
});

console.log(`browser server ready: ws://<host>:${port}/`);
console.log(`local endpoint:      ${server.wsEndpoint()}`);

let stopping = false;

// СМЕРТЬ БРАУЗЕРА — ЭТО ОТКАЗ, А НЕ КОНЕЦ РАБОТЫ. Тяжёлая выдача (замер
// 16.09.2026: страница Яндекса) убивает Firefox, сервер закрывается следом, и
// node выходил с кодом НОЛЬ — то есть «отработал и закончил». Для docker с
// restart: on-failure это означало «не перезапускать», а у всех подключённых
// соединение рвалось с «socket hang up», неотличимым от сетевой икоты.
// Ненулевой код превращает это в отказ, который видно и по которому
// перезапускаются.
server.on('close', () => {
  if (stopping)
    return;

  console.error('browser server closed on its own: the browser inside it died');
  process.exit(1);
});

const stop = async (signal) => {
  stopping = true;

  console.log(`got ${signal}, closing`);
  await server.close();
  process.exit(0);
};

process.on('SIGTERM', () => stop('SIGTERM'));
process.on('SIGINT', () => stop('SIGINT'));
