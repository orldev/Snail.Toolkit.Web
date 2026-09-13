// Что проверяем и чем это отличается от того, что мы уже умели.
//
// Первое — правда ли сборка молчит о себе. Это измеряется без сети, внутри
// контейнера, и сравнением: рядом поднимается штатный Firefox от Playwright,
// который говорит `webdriver === true`. Две колонки в одном прогоне — это
// контроль, а не одиночное наблюдение.
//
// Второе — важнее первого. Стелс-плагин тоже даёт `false`, но подменой: у
// свойства появляется геттер, и его toString выдаёт исходник подделки. У
// скомпилированной лжи геттер нативный. Колонка «нативный» и есть ответ на
// вопрос, ради которого собирали.
//
// Третье — сеть, и оно включается отдельно (NET=1), потому что отвечает не на
// вопрос о сборке, а на вопрос о том, как нас читает Google. Эти вопросы
// смешивать нельзя: контейнер ходит с чистым профилем без куков, а это ровно
// та поза, которая подозрительна сама по себе.

import { firefox } from 'playwright';
import { existsSync } from 'node:fs';

const OURS = '/browser/firefox/firefox';
const headless = process.env.HEADFUL !== '1';

async function probe(executablePath) {
  const browser = await firefox.launch({ headless, ...(executablePath ? { executablePath } : {}) });
  try {
    const page = await browser.newPage();
    await page.goto('about:blank');
    return await page.evaluate(() => {
      const own = Object.getOwnPropertyDescriptor(Navigator.prototype, 'webdriver');
      const source = own?.get ? own.get.toString() : null;
      return {
        value: String(navigator.webdriver),
        native: source === null ? null : /\[native code\]/.test(source),
        source: source === null ? 'нет геттера' : source.replace(/\s+/g, ' ').slice(0, 70),
        agent: navigator.userAgent,
        // Эти три уносит с собой pref("pdfjs.disabled", true) из playwright.cfg.
        // Обычный Firefox отвечает true / 5 / 2, и отличие от него — маркер.
        pdf: `${navigator.pdfViewerEnabled} / ${navigator.plugins.length} / ${navigator.mimeTypes.length}`,
        // ui.use_standins_for_native_colors подменяет системные цвета CSS на
        // обобщённые — одни и те же у всех. На рабочем столе разница видна сразу;
        // в контейнере без окружения может не быть вовсе, и это тоже ответ.
        colors: (() => {
          const probe = document.createElement('div');
          document.body.appendChild(probe);
          const read = ['Highlight', 'AccentColor'].map((name) => {
            probe.style.color = name;
            return getComputedStyle(probe).color;
          });
          probe.remove();
          return read.join('  ');
        })(),
        // gfx.color_management.mode меняет отрисовку. Проверяется только
        // сравнением: у нашей сборки 2, у штатной 0 — хеши обязаны разойтись,
        // и если они совпали, правка не взялась.
        canvas: (() => {
          const canvas = document.createElement('canvas');
          canvas.width = 60;
          canvas.height = 20;
          const ctx = canvas.getContext('2d');
          ctx.fillStyle = '#f60';
          ctx.fillRect(0, 0, 60, 20);
          ctx.fillStyle = '#069';
          ctx.font = '14px serif';
          ctx.fillText('Snail', 2, 15);
          const data = canvas.toDataURL();
          let hash = 0;
          for (let i = 0; i < data.length; i++) hash = (hash * 31 + data.charCodeAt(i)) | 0;
          return hash.toString(16);
        })(),
      };
    });
  } finally {
    await browser.close();
  }
}

function row(label, seen) {
  const verdict = seen instanceof Error ? `ОШИБКА: ${seen.message.split('\n')[0].slice(0, 70)}`
    : `webdriver=${seen.value}  нативный геттер=${seen.native}`;
  console.log(`  ${label.padEnd(22)} ${verdict}`);
  if (!(seen instanceof Error)) {
    console.log(`  ${''.padEnd(22)} ${seen.source}`);
    console.log(`  ${''.padEnd(22)} ${seen.agent}`);
    console.log(`  ${''.padEnd(22)} pdfViewerEnabled/plugins/mimeTypes: ${seen.pdf}   (обычный Firefox: true / 5 / 2)`);
    console.log(`  ${''.padEnd(22)} Highlight / AccentColor: ${seen.colors}`);
    console.log(`  ${''.padEnd(22)} отпечаток canvas: ${seen.canvas}`);
  }
}

console.log(`\nРежим: ${headless ? 'headless' : 'окно (xvfb)'}\n`);
console.log('navigator.webdriver');

if (!existsSync(OURS)) {
  console.error(`  Не найден ${OURS} — архив не распакован.`);
  process.exit(1);
}

const mine = await probe(OURS).catch((error) => error);
const stock = await probe(undefined).catch((error) => error);

row('наша сборка', mine);
row('штатный Playwright', stock);

if (mine instanceof Error) {
  console.error('\nСборка не поднялась. Всё остальное измерять нечего.');
  process.exit(1);
}

if (mine.value !== 'false') {
  console.error('\nПравка не применилась: сборка по-прежнему объявляет себя.');
  process.exit(1);
}

if (mine.native !== true) {
  console.error('\nЗначение верное, но геттер не нативный — это подмена, а не сборка.');
  process.exit(1);
}

console.log('\n  Ложь стала фактом компиляции: значение false, геттер нативный, следа подмены нет.');

// Тёплый вход: профиль, который живёт между запусками, и путь человека.
//
// Три холодных захода подряд получили /sorry/ — и наша сборка, и штатная, с
// адреса, который у Google на хорошем счету. Значит решают не бинарник и не
// адрес. Что остаётся общего у вчерашнего единственного успеха и у всех
// провалов наоборот — сессия: куки, согласие, путь входа.
//
// Здесь проверяется ровно это и только это. Профиль постоянный (том /profile),
// вход через главную, запрос набирается в поле с задержками, а не подставляется
// в адрес. Второй запуск с тем же томом будет уже по-настоящему тёплым — в нём
// лягут NID и SOCS, если первый до них дойдёт.
//
// Скриншот кладётся в /out: отказ отказу рознь, и стена согласия, капча и
// выдача различаются глазом за секунду, а по коду ответа — нет.

// Среда, а не сборка. Всё, что ниже, одинаково у обеих сборок — это свойства
// контейнера, и менять их настройками playwright.cfg бессмысленно. Печатаются
// они потому, что несколько строк здесь способны перевесить всё, что мы
// правили выше: браузер, который представляется американцем и живёт по UTC,
// противоречит сам себе, и это противоречие читается одной строкой.

async function environment() {
  const browser = await firefox.launch({ headless, executablePath: OURS });
  try {
    const page = await browser.newPage();
    await page.goto('about:blank');
    return await page.evaluate(() => {
      const gl = (() => {
        const ctx = document.createElement('canvas').getContext('webgl');
        if (!ctx) return 'недоступен';
        const info = ctx.getExtension('WEBGL_debug_renderer_info');
        return info
          ? `${ctx.getParameter(info.UNMASKED_VENDOR_WEBGL)} / ${ctx.getParameter(info.UNMASKED_RENDERER_WEBGL)}`
          : 'расширение недоступно';
      })();

      // Шрифт считается имеющимся, если текст им меряется иначе, чем запасным.
      const measured = (() => {
        const ctx = document.createElement('canvas').getContext('2d');
        const sample = 'mmmmmmmmmmlli';
        const base = ['monospace', 'sans-serif', 'serif'].map((family) => {
          ctx.font = `72px ${family}`;
          return ctx.measureText(sample).width;
        });
        const named = ['Arial', 'Helvetica', 'Times New Roman', 'Courier New', 'Georgia',
          'Verdana', 'Tahoma', 'Trebuchet MS', 'Segoe UI', 'Roboto', 'Ubuntu', 'DejaVu Sans'];
        return named.filter((font, index) => ['monospace', 'sans-serif', 'serif'].some((family, slot) => {
          ctx.font = `72px "${font}", ${family}`;
          return ctx.measureText(sample).width !== base[slot];
        }));
      })();

      return {
        'часовой пояс': `${Intl.DateTimeFormat().resolvedOptions().timeZone}, смещение ${-new Date().getTimezoneOffset() / 60} ч`,
        'языки': navigator.languages.join(','),
        'WebGL': gl,
        'экран': `${screen.width}x${screen.height}, глубина ${screen.colorDepth}, dpr ${devicePixelRatio}`,
        'ядер / память': `${navigator.hardwareConcurrency} / ${navigator.deviceMemory ?? 'не сообщает'}`,
        'шрифтов найдено': `${measured.length} из 12: ${measured.join(', ') || 'ни одного'}`,
        'document.hasFocus()': String(document.hasFocus()),
        'уведомления': typeof Notification === 'undefined' ? 'нет API' : Notification.permission,
        'касаний': navigator.maxTouchPoints,
      };
    });
  } finally {
    await browser.close();
  }
}

console.log('\nСреда контейнера (у обеих сборок одинакова)\n');
const around = await environment().catch((error) => ({ 'ошибка': error.message.split('\n')[0].slice(0, 70) }));
for (const [key, value] of Object.entries(around)) console.log(`  ${key.padEnd(22)} ${value}`);

if (around['часовой пояс']?.startsWith('UTC')) {
  console.log('\n  ! Пояс UTC. Для адреса из США это противоречие: живых машин в UTC там нет.');
  console.log('    Лечится запуском контейнера с TZ, например -e TZ=America/Chicago.');
}
if (/llvmpipe|softpipe|SwiftShader|Mesa/i.test(around['WebGL'] ?? '')) {
  console.log('\n  ! WebGL программный. Настольная машина почти всегда отдаёт имя видеокарты;');
  console.log('    в контейнере без GPU это не лечится настройками.');
}

if (process.env.WARM === '1') {
  // STOCK=1 ведёт поиск ШТАТНЫМ Firefox от Playwright вместо нашей сборки.
  // Это решающая проверка: если он проходит с исправленным поясом, своя сборка
  // для Google не нужна вовсе, и держать её в продакшене незачем.
  const stock = process.env.STOCK === '1';

  const context = await firefox.launchPersistentContext('/profile', {
    ...(stock ? {} : { executablePath: OURS }),
    headless,
    locale: 'en-US',
    viewport: { width: 1280, height: 800 },
  });

  // SPOOF=1 подменяет navigator.webdriver скриптом — так делают стелс-плагины.
  // Значение становится тем же, что даёт наша сборка, но подмена ВИДНА: геттер
  // перестаёт быть нативным, и его toString выдаёт исходник. Этим и разводится
  // вопрос, ради которого собирали Firefox: важно значение или его нативность.
  if (process.env.SPOOF === '1')
    await context.addInitScript(() => {
      Object.defineProperty(Navigator.prototype, 'webdriver', { get: () => false, configurable: true });
    });

  const page = context.pages()[0] ?? await context.newPage();

  // DIRECT=1 меняет РОВНО ОДНО: путь входа. Вместо главной с набором в поле —
  // сразу /search?q=…, как ходит провайдер тулкита. Сборка, профиль и адрес
  // остаются те же, что дали шесть выдач; поэтому ответ читается как ответ про
  // путь входа, а не про три оси разом — ошибка, уже стоившая одного вывода.
  const direct = process.env.DIRECT === '1';
  const query = 'postgresql documentation';

  // Вердикт снимается ОДНИМ И ТЕМ ЖЕ разбором для обоих путей: плечи сравнимы
  // только тогда, когда отличаются входом, а не способом прочесть результат.
  const verdict = async () => {
    const title = await page.title();
    return page.url().includes('/sorry/') ? 'ОТКАЗ /sorry/'
      : /captcha|robot|unusual traffic/i.test(title) ? `КАПЧА: ${title}`
      : `прошло, ссылок с заголовком: ${await page.locator('a:has(h3)').count()}`;
  };

  const announced = () => page.evaluate(() => {
    const own = Object.getOwnPropertyDescriptor(Navigator.prototype, 'webdriver');
    const source = own?.get ? own.get.toString() : '';
    return `${navigator.webdriver}, геттер ${/\[native code\]/.test(source) ? 'нативный' : 'ПОДМЕНЁН'}`;
  });

  try {
    console.log(`\n${direct ? 'ПРЯМОЙ вход на /search' : 'Тёплый вход'} — ${stock ? 'ШТАТНЫЙ Playwright' : 'наша сборка'}${process.env.SPOOF === '1' ? ' + ПОДМЕНА webdriver' : ''}\n`);

    if (direct) {
      await page.goto(`https://www.google.com/search?q=${encodeURIComponent(query)}`,
        { waitUntil: 'domcontentloaded', timeout: 45000 });
      await page.waitForTimeout(4000);
      console.log(`  webdriver на странице    ${await announced()}`);
      console.log(`  выдача                   ${await verdict()}`);
      console.log(`                           ${page.url().slice(0, 90)}`);
    } else {
      await page.goto('https://www.google.com/', { waitUntil: 'domcontentloaded', timeout: 45000 });
      await page.waitForTimeout(3000);
      console.log(`  webdriver на странице    ${await announced()}`);
      console.log(`  главная                  ${page.url()}`);
      console.log(`                           ${await page.title()}`);

      // Стена согласия приходит не всегда и на разных языках — отсюда список, а не
      // один селектор, и мягкий пропуск, если ничего не совпало.
      for (const selector of [
        'button:has-text("Accept all")',
        'button:has-text("Reject all")',
        'button#L2AGLb',
        'div[role="none"] button:has-text("Принять все")',
      ]) {
        const button = page.locator(selector).first();
        if (await button.count() && await button.isVisible().catch(() => false)) {
          await button.click({ timeout: 5000 }).catch(() => {});
          console.log(`  согласие                 нажато: ${selector}`);
          await page.waitForTimeout(2500);
          break;
        }
      }

      const field = page.locator('textarea[name="q"], input[name="q"]').first();
      if (!(await field.count())) {
        console.log('  поле поиска              НЕ НАЙДЕНО — до выдачи не дошли');
      } else {
        await field.click();
        await page.waitForTimeout(600);
        await field.type(query, { delay: 140 });
        await page.waitForTimeout(900);
        await field.press('Enter');
        await page.waitForLoadState('domcontentloaded', { timeout: 45000 }).catch(() => {});
        await page.waitForTimeout(4000);

        console.log(`  выдача                   ${await verdict()}`);
        console.log(`                           ${page.url().slice(0, 90)}`);
      }
    }

    // Widevine качается по сети при первом запуске и остаётся в профиле. Здесь
    // ему для этого есть где остаться, в отличие от разовых контекстов выше.
    const widevine = await page.evaluate(() => navigator.requestMediaKeySystemAccess('com.widevine.alpha', [{
      initDataTypes: ['cenc'],
      videoCapabilities: [{ contentType: 'video/mp4; codecs="avc1.42E01E"' }],
    }]).then(() => 'доступна').catch((error) => `нет: ${error.name}`));
    console.log(`  Widevine                 ${widevine}   (на настольном Firefox: доступна)`);

    const cookies = await context.cookies('https://www.google.com');
    console.log(`  куки после захода        ${cookies.map((c) => c.name).join(', ') || 'ни одной'}`);

    const file = direct ? '/out/direct.png' : '/out/warm.png';
    const shot = await page.screenshot({ path: file, fullPage: false })
      .then(() => file)
      .catch((error) => `НЕ СОХРАНЁН: ${error.message.split('\n')[0].slice(0, 60)}`);
    console.log(`  снимок                   ${shot}`);
  } catch (error) {
    console.log(`  ошибка: ${error.message.split('\n')[0].slice(0, 90)}`);
  } finally {
    await context.close();
  }

  console.log();
  process.exit(0);
}

if (process.env.NET !== '1') {
  console.log('\nСеть не трогали.');
  console.log('  NET=1   — холодный замер с контролем: наша, штатная, наша.');
  console.log('  WARM=1  — тёплый вход: постоянный профиль, главная, набор в поле.');
  console.log('  WARM=1 DIRECT=1 — тот же профиль, но сразу /search: путь входа как у тулкита.\n');
  process.exit(0);
}

// Сеть с контролем, а не одиночным наблюдением.
//
// Вчерашний неверный вывод про Google родился из того, что отказ наблюдался
// много раз, но всегда одним клиентом: различить «отказывают клиенту» и
// «отказывают адресу» такой ряд не может. Здесь в Google идут по очереди наша
// сборка, штатный Playwright и снова наша — с одного адреса, с интервалом.
//
// Что из чего следует:
//   пройдёт / откажут / пройдёт  — решает webdriver, сборка имеет смысл;
//   откажут / откажут / откажут  — webdriver ни при чём, дело в другом;
//   пройдёт / пройдёт / *        — не блокировали вовсе, мерить было нечего;
//   пройдёт / откажут / откажут  — похоже на частоту, а не на клиента.
//
// Пауза между заходами — не вежливость, а условие измерения: частота сама по
// себе вызывает отказ, и без неё третий заход померяет её, а не патч.

const pause = Number(process.env.PAUSE ?? 60) * 1000;

async function ask(executablePath, url, google = true) {
  const browser = await firefox.launch({ headless, ...(executablePath ? { executablePath } : {}) });
  try {
    const page = await browser.newPage();
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45000 });
    await page.waitForTimeout(2500);

    const title = await page.title();
    if (page.url().includes('/sorry/')) return 'ОТКАЗ /sorry/';
    if (/captcha|robot|unusual traffic/i.test(title)) return `КАПЧА: ${title}`;
    return google
      ? `прошло, ссылок с заголовком: ${await page.locator('a:has(h3)').count()}`
      : `прошло: ${title}`;
  } catch (error) {
    return `ошибка: ${error.message.split('\n')[0].slice(0, 70)}`;
  } finally {
    await browser.close();
  }
}

const query = 'postgresql+documentation';

console.log('\nСеть\n');
console.log(`  ${'Brave, наша сборка'.padEnd(26)} ${await ask(OURS, `https://search.brave.com/search?q=${query}`, false)}`);
console.log('  (Brave здесь только чтобы убедиться, что машинерия вообще ходит в сеть)\n');

for (const [label, executablePath] of [
  ['Google, наша сборка', OURS],
  ['Google, штатный', undefined],
  ['Google, наша сборка', OURS],
]) {
  await new Promise((resolve) => setTimeout(resolve, pause));
  console.log(`  ${label.padEnd(26)} ${await ask(executablePath, `https://www.google.com/search?q=${query}`)}`);
}

console.log();
