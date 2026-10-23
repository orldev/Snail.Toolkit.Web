using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Search;
using Snail.Toolkit.Web.Search.Browser;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Benchmarks;

/// <summary>What a page costs on the library's own paths once the browser has handed it over.</summary>
/// <remarks>
/// Three roads a page takes: a plain read, which answers its text and title; a crawl, which also reads the page's
/// links, its canonical address and a digest of its text; and a search engine's results page, whose links are
/// all anyone reads. The audit counted two, four and three parses of the same markup on them.
/// </remarks>
[MemoryDiagnoser]
public class Browsing
{
    private ServiceProvider _pages = null!;

    private ServiceProvider _results = null!;

    private IPageReader _reader = null!;

    private ICrawler _crawler = null!;

    private ISearchProvider _engine = null!;

    private Link _address = null!;

    private CrawlScope _scope = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var html = Page.Build();

        _pages = Browserless.Serving(html);
        _results = Browserless.Serving(ResultsPage.Build());

        _reader = _pages.GetRequiredService<IPageReader>();
        _crawler = _pages.GetRequiredService<ICrawler>();
        _engine = _results.GetServices<ISearchProvider>().OfType<Brave>().Single();

        _address = Link.Of(Page.Address.AbsoluteUri);
        _scope = _crawler.ScopeOf(_address) with { MaxPages = 1, MaxDepth = 0 };

        await AgreesAsync(html);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pages.Dispose();
        _results.Dispose();
    }

    [Benchmark]
    public async Task<int> PageRead()
    {
        var outcome = await _reader.ReadAsync(_address, CancellationToken.None);

        return outcome.Value.Text.Length;
    }

    [Benchmark]
    public async Task<int> CrawledPage()
    {
        var report = await _crawler.CrawlAsync(_scope);

        return report.Pages.Count;
    }

    [Benchmark]
    public async Task<int> EnginePage()
    {
        var answer = await _engine.FindAsync(ResultsPage.Query);

        return answer.Value.Count;
    }

    /// <summary>Refuses to time roads that answer differently from the extraction asked one call at a time.</summary>
    private async Task AgreesAsync(string html)
    {
        var text = new TextExtraction();
        var expected = text.Clean(html);
        var title = text.Title(html);

        var read = (await _reader.ReadAsync(_address, CancellationToken.None)).Value;
        var report = await _crawler.CrawlAsync(_scope);
        var crawled = report.Pages.Single().Value;
        var answer = await _engine.FindAsync(ResultsPage.Query);

        if (read.Text != expected || read.Title != title)
            throw new InvalidOperationException("A page read answered another text or title than the extraction.");

        if (crawled.Text != ContentBudget.Of(20_000).Apply(expected).Text || crawled.Title != title)
            throw new InvalidOperationException("A crawled page answered another text or title than the extraction.");

        if (!answer.IsSuccess || answer.Value.Count != 20)
            throw new InvalidOperationException("The engine did not answer the twenty results its page carries.");
    }
}
