using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping;
using Snail.Toolkit.Web.Scraping.Sitemaps;
using Snail.Toolkit.Web.Sitemaps;

namespace Snail.Toolkit.Web.Benchmarks;

/// <summary>What a site's own list of pages costs: the sitemap read into entries, and the download that hands it over.</summary>
/// <remarks>
/// The download goes through the library's own HTTP source to a listener on the loopback interface, plain and
/// gzipped, so the timing carries a real response stream; the listener's share is the same on either side of a
/// change. The setup refuses to time downloads whose entries differ from the document read in memory.
/// </remarks>
[MemoryDiagnoser]
public class Listing
{
    private readonly HttpListener _listener = new();

    private string _xml = null!;

    private HttpSitemapSource _source = null!;

    private Uri _plain = null!;

    private Uri _gzipped = null!;

    private Task _serving = Task.CompletedTask;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _xml = SitemapDocument.Build();

        var raw = Encoding.UTF8.GetBytes(_xml);
        var port = FreePort();

        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _serving = ServeAsync(raw, Gzipped(raw));

        _plain = new Uri($"http://127.0.0.1:{port}/sitemap.xml");
        _gzipped = new Uri($"http://127.0.0.1:{port}/sitemap.xml.gz");
        _source = new HttpSitemapSource(Options.Create(new CrawlOptions()), NullLogger<HttpSitemapSource>.Instance);

        var parsed = SitemapFile.Parse(_xml);
        var plain = await _source.ReadAsync(_plain, null);
        var gzipped = await _source.ReadAsync(_gzipped, null);

        if (parsed.Pages.Count != SitemapDocument.Addresses
            || !parsed.Pages.SequenceEqual(plain.Pages)
            || !parsed.Pages.SequenceEqual(gzipped.Pages))
            throw new InvalidOperationException("The downloads did not answer the entries the document lists.");
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        _source.Dispose();
        _listener.Stop();
        _listener.Close();

        await _serving;
    }

    [Benchmark]
    public int Parse() => SitemapFile.Parse(_xml).Pages.Count;

    [Benchmark]
    public async Task<int> Download()
    {
        var file = await _source.ReadAsync(_plain, null);

        return file.Pages.Count;
    }

    [Benchmark]
    public async Task<int> GzippedDownload()
    {
        var file = await _source.ReadAsync(_gzipped, null);

        return file.Pages.Count;
    }

    private async Task ServeAsync(byte[] plain, byte[] gzipped)
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            var body = context.Request.Url!.AbsolutePath.EndsWith(".gz", StringComparison.Ordinal) ? gzipped : plain;

            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
    }

    private static byte[] Gzipped(byte[] raw)
    {
        using var kept = new MemoryStream();

        using (var zip = new GZipStream(kept, CompressionLevel.Optimal, leaveOpen: true))
            zip.Write(raw);

        return kept.ToArray();
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
