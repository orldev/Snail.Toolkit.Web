using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Scraping.Sitemaps;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// Covers downloading a sitemap: the bytes a site serves, read into the pages it lists.
/// </summary>
public sealed class HttpSitemapSourceTests : IDisposable
{
    private const string Urlset =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
        + "<url><loc>https://example.com/grain</loc></url><url><loc>https://example.com/barley</loc></url></urlset>";

    private static readonly byte[] Mark = [0xEF, 0xBB, 0xBF];

    private readonly Served _site = new();

    private readonly HttpSitemapSource _source =
        new(Options.Create(new CrawlOptions()), NullLogger<HttpSitemapSource>.Instance);

    /// <summary>
    /// Editors and CMSs save a sitemap with a UTF-8 byte-order mark often enough, and such a sitemap used to read
    /// as no sitemap at all: the mark was decoded into a character ahead of the document, which XML refuses.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_SitemapWithAByteOrderMark_ReadsAsTheSameDocumentWithoutIt(bool isGzipped)
    {
        var plain = Encoding.UTF8.GetBytes(Urlset);
        var marked = await _source.ReadAsync(_site.Serving("/marked.xml", Packed([.. Mark, .. plain], isGzipped)), null);
        var unmarked = await _source.ReadAsync(_site.Serving("/plain.xml", Packed(plain, isGzipped)), null);

        Assert.Equal(
            ["https://example.com/grain", "https://example.com/barley"],
            marked.Pages.Select(page => page.Address.Canonical));
        Assert.Equal(unmarked.Pages, marked.Pages);
    }

    /// <summary>Only the one mark is taken; a second is a character of the text, and the document is malformed.</summary>
    [Fact]
    public async Task ReadAsync_SitemapWithTwoByteOrderMarks_ReadsAsNoSitemap()
    {
        var file = await _source.ReadAsync(_site.Serving("/twice.xml", [.. Mark, .. Mark, .. Encoding.UTF8.GetBytes(Urlset)]), null);

        Assert.Empty(file.Pages);
    }

    public void Dispose()
    {
        _source.Dispose();
        _site.Dispose();
    }

    private static byte[] Packed(byte[] raw, bool isGzipped)
    {
        if (!isGzipped)
            return raw;

        using var kept = new MemoryStream();

        using (var zip = new GZipStream(kept, CompressionLevel.Optimal, leaveOpen: true))
            zip.Write(raw);

        return kept.ToArray();
    }

    /// <summary>A listener on the loopback interface answering each path with the bytes it was given.</summary>
    private sealed class Served : IDisposable
    {
        private readonly HttpListener _listener = new();

        private readonly Dictionary<string, byte[]> _bodies = new(StringComparer.Ordinal);

        private readonly string _root;

        public Served()
        {
            _root = $"http://127.0.0.1:{FreePort()}/";
            _listener.Prefixes.Add(_root);
            _listener.Start();

            _ = AnswerAsync();
        }

        public Uri Serving(string path, byte[] body)
        {
            lock (_bodies)
                _bodies[path] = body;

            return new Uri($"{_root.TrimEnd('/')}{path}");
        }

        public void Dispose() => _listener.Close();

        private async Task AnswerAsync()
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

                byte[]? body;

                lock (_bodies)
                    _bodies.TryGetValue(context.Request.Url!.AbsolutePath, out body);

                context.Response.StatusCode = body is null ? 404 : 200;
                context.Response.ContentLength64 = body?.Length ?? 0;
                await context.Response.OutputStream.WriteAsync(body ?? []);
                context.Response.Close();
            }
        }

        private static int FreePort()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);

            probe.Start();

            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
    }
}
