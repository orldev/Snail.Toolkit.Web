using Snail.Toolkit.Web.Probes;

// Probes are called by name: dotnet run --project probes -- <probe> [server address]
//
// The default address is ws://127.0.0.1:3000/, the tunnel to the container with our own
// build. What each one measures and how to read its output is in probes/README.md.

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
    "markup" => Markup.ReportAsync,
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
    Console.WriteLine("  markup       чего стоит разбор разметки и сколько его платится дважды");
    Console.WriteLine();
    Console.WriteLine("  dotnet run --project probes -- <проба> [ws://адрес-сервера/]");

    return 1;
}

await chosen(endpoint);

return 0;
