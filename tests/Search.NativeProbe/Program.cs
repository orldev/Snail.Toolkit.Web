using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Politeness;
using Snail.Toolkit.Web.Search;
using Snail.Toolkit.Web.Search.Extensions;
using Snail.Toolkit.Web.Text;

const string html = """
    <html><head><title>Native</title><link rel="canonical" href="https://example.com/page"></head>
    <body><p>Read <b>under</b> Native AOT.</p><a href="/next?utm_source=probe">next</a></body></html>
    """;

var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Web:Search:MaxResultsPerProvider"] = "7",
    ["Web:Search:Timeout"] = "00:00:12",
    ["Web:Politeness:MinDelayPerHost"] = "00:00:03",
    ["Web:Politeness:HostDelays:example.com"] = "00:00:09",
}).Build();

var services = new ServiceCollection().AddLogging();
services.AddSearch(configuration);

using var provider = services.BuildServiceProvider();
var search = provider.GetRequiredService<IOptions<SearchOptions>>().Value;
var politeness = provider.GetRequiredService<IOptions<PolitenessOptions>>().Value;
var engines = provider.GetServices<ISearchProvider>().Count();

var address = new Uri("https://example.com/page");
var text = provider.GetRequiredService<ITextExtraction>().Clean(html);
var links = provider.GetRequiredService<ILinkExtraction>().From(html, address);
var verdict = provider.GetRequiredService<IBlockDetector>().Inspect(html, address.AbsoluteUri);

var isBound = search.MaxResultsPerProvider == 7
    && search.Timeout == TimeSpan.FromSeconds(12)
    && politeness.MinDelayPerHost == TimeSpan.FromSeconds(3)
    && politeness.GapFor("example.com") == TimeSpan.FromSeconds(9);
var isRead = text == "Read under Native AOT.\nnext";
var isLinked = links.Count == 1 && links[0].Canonical == Link.Of("https://example.com/next").Canonical;

Console.WriteLine($"settings bound: {isBound}; {engines} provider(s); text \"{text}\"; {links.Count} link(s); blocked: {verdict.IsBlocked}");

return isBound && engines > 0 && isRead && isLinked && !verdict.IsBlocked ? 0 : 1;
