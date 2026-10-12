using BenchmarkDotNet.Attributes;
using Snail.Toolkit.Web.Detection;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Benchmarks;

/// <summary>What every page read costs once it has arrived: its text and title, its links, and the block check.</summary>
/// <remarks>
/// The audit's hot paths of the core package. A crawled page is judged for a block, cleaned to text, titled and
/// searched for links; each of those parses the markup on its own today, which is what these timings weigh. The
/// addresses are the page's own, read into their comparable form as every link, sitemap entry and search result is.
/// </remarks>
[MemoryDiagnoser]
public class Reading
{
    private readonly TextExtraction _text = new();

    private readonly LinkExtraction _links = new();

    private readonly BlockDetector _blocks = new();

    private string _html = null!;

    private string[] _addresses = null!;

    [GlobalSetup]
    public void Setup()
    {
        _html = Page.Build();

        var found = _links.From(_html, Page.Address);
        _addresses = [.. found.Select(link => link.Text)];
    }

    [Benchmark]
    public int TextAndTitle()
    {
        var text = _text.Clean(_html);
        var title = _text.Title(_html);

        return text.Length + (title?.Length ?? 0);
    }

    [Benchmark]
    public int LinksAndCanonical()
    {
        var found = _links.From(_html, Page.Address);
        var canonical = _links.CanonicalOf(_html, Page.Address);

        return found.Count + (canonical is null ? 0 : 1);
    }

    [Benchmark]
    public int LinkOf()
    {
        var length = 0;

        foreach (var address in _addresses)
            length += Link.Of(address).Canonical.Length;

        return length;
    }

    [Benchmark]
    public BlockVerdict Blocks() => _blocks.Inspect(_html, Page.Address.AbsoluteUri);
}
