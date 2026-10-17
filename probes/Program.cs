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
    Console.WriteLine("Probes:");
    Console.WriteLine("  fingerprint  the build's network fingerprint: JA3/JA4, HTTP/2, header order");
    Console.WriteLine("  wrappers     whether a link wrapper resolves by a redirect, and whether the basic results page helps");
    Console.WriteLine("  resolving    what resolving costs: whether it needs a session and whether the engine takes it in a row");
    Console.WriteLine("  counting     whether the engine counts requests rather than pages");
    Console.WriteLine("  answering    the whole search, as a consumer calls it");
    Console.WriteLine("  contexts     what a context costs beside a process: memory and opening time");
    Console.WriteLine("  markup       what parsing markup costs and how much of it is paid twice");
    Console.WriteLine();
    Console.WriteLine("  dotnet run --project probes -- <probe> [ws://server-address/]");

    return 1;
}

await chosen(endpoint);

return 0;
