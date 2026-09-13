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

const stop = async (signal) => {
  console.log(`got ${signal}, closing`);
  await server.close();
  process.exit(0);
};

process.on('SIGTERM', () => stop('SIGTERM'));
process.on('SIGINT', () => stop('SIGINT'));
