using Snail.Toolkit.Web.Probes;

// Пробы вызываются по имени: dotnet run --project probes -- <проба> [адрес сервера]
//
// Адрес по умолчанию — ws://127.0.0.1:3000/, то есть туннель к контейнеру с нашей
// сборкой. Что меряет каждая и как читать её вывод — в probes/README.md.

var name = args.Length > 0 ? args[0] : string.Empty;
var endpoint = args.Length > 1 ? args[1] : "ws://127.0.0.1:3000/";

Func<string, Task>? chosen = name switch
{
    "fingerprint" => Fingerprint.ReportAsync,
    "wrappers" => Wrappers.ReportAsync,
    "resolving" => Resolving.ReportAsync,
    "counting" => Counting.ReportAsync,
    "answering" => Answering.ReportAsync,
    "contexts" => Contexts.ReportAsync,
    _ => null
};

if (chosen is null)
{
    Console.WriteLine("Пробы:");
    Console.WriteLine("  fingerprint  сетевой отпечаток сборки: JA3/JA4, HTTP/2, порядок заголовков");
    Console.WriteLine("  wrappers     раскрывается ли обёртка ссылки редиректом, и помогает ли упрощённая выдача");
    Console.WriteLine("  resolving    чем платит раскрытие: нужна ли сессия и терпит ли движок подряд");
    Console.WriteLine("  counting     считает ли движок запросы, а не страницы");
    Console.WriteLine("  answering    поиск целиком, так как его позовёт потребитель");
    Console.WriteLine("  contexts     чего стоит контекст рядом с процессом: память и время открытия");
    Console.WriteLine();
    Console.WriteLine("  dotnet run --project probes -- <проба> [ws://адрес-сервера/]");

    return 1;
}

await chosen(endpoint);

return 0;
