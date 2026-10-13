using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Http;
using Snail.Toolkit.Web.Sitemaps;

namespace Snail.Toolkit.Web.Scraping.Sitemaps;

/// <summary>
/// Downloads a sitemap over HTTP, decompressing the gzipped ones.
/// </summary>
/// <remarks>
/// Compression is decided by the first two bytes rather than by the file name or the content type. A sitemap is
/// routinely served as <c>.xml.gz</c> with no <c>Content-Encoding</c>, which no HTTP client decompresses on its
/// own, and just as routinely served gzipped from a name that says <c>.xml</c>.
/// <para>
/// The read is capped. Compressed XML expands by twenty to one and the file is written by the site being read,
/// so an unbounded read is a memory limit set by someone else.
/// </para>
/// <para>
/// The cap holds on the network too: only the headers are awaited and the body is read under it. Awaited whole,
/// as the client does by default, the body was buffered before the cap was ever consulted — measured on
/// 2026-10-13, an answer of 50 MB cost 95 MB per read under a ceiling of 1 000 bytes. A body that breaks off
/// while it is read fails as an <see cref="IOException"/>, and reads as no sitemap, as a failed request does.
/// </para>
/// </remarks>
public sealed class HttpSitemapSource(IOptions<CrawlOptions> options, ILogger<HttpSitemapSource> logger)
    : ISitemapSource, IDisposable
{
    private const int GzipFirstByte = 0x1F;
    private const int GzipSecondByte = 0x8B;

    private readonly CrawlOptions _options = options.Value;
    private readonly ProxiedHttp _http = new();

    /// <inheritdoc />
    public async Task<SitemapFile> ReadAsync(Uri address, ProxyEndpoint? proxy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_options.SitemapTimeout);

        try
        {
            using var response = await _http.For(proxy).GetAsync(address, HttpCompletionOption.ResponseHeadersRead, budget.Token);

            if (!response.IsSuccessStatusCode)
            {
                Log.SitemapAnswered(logger, address, (int)response.StatusCode);

                return SitemapFile.Empty;
            }

            return await ParsedAsync(response, budget.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            Log.SitemapUnreadable(logger, exception, address);

            return SitemapFile.Empty;
        }
    }

    /// <summary>Reads the sitemap from the bytes kept, expanding them first when they are gzipped.</summary>
    /// <remarks>
    /// The bytes are decoded as UTF-8 while the document is read, as they were decoded whole before: the kept
    /// bytes used to be copied into an array of their own and that array into a string twice its size, three
    /// copies of up to <see cref="CrawlOptions.MaxSitemapBytes"/> on the large-object heap for one read. The
    /// declared encoding is not consulted and nothing else is detected.
    /// <para>
    /// A leading UTF-8 byte-order mark is skipped. It used to be decoded into a character ahead of the document,
    /// which the XML reader refuses, so a sitemap saved with the mark — plenty of editors and CMSs write one —
    /// read as no sitemap at all. Only that one mark is taken: a UTF-16 mark is still read as UTF-8, as before.
    /// </para>
    /// </remarks>
    private async Task<SitemapFile> ParsedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        await using var raw = await BoundedAsync(body, ct);

        if (!IsGzipped(raw))
            return Parsed(raw);

        await using var expanding = new GZipStream(raw, CompressionMode.Decompress);
        await using var expanded = await BoundedAsync(expanding, ct);

        return Parsed(expanded);
    }

    private static bool IsGzipped(MemoryStream raw)
        => raw.Length >= 2 && raw.GetBuffer() is [GzipFirstByte, GzipSecondByte, ..];

    private static SitemapFile Parsed(MemoryStream kept)
    {
        using var text = new StreamReader(kept, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

        return SitemapFile.Read(text);
    }

    private async Task<MemoryStream> BoundedAsync(Stream source, CancellationToken ct)
    {
        var kept = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (kept.Length < _options.MaxSitemapBytes)
        {
            var read = await source.ReadAsync(buffer, ct);

            if (read == 0)
                return Rewound(kept);

            await kept.WriteAsync(buffer.AsMemory(0, (int)Math.Min(read, _options.MaxSitemapBytes - kept.Length)), ct);
        }

        Log.SitemapCut(logger, _options.MaxSitemapBytes, CrawlOptions.SectionName, nameof(CrawlOptions.MaxSitemapBytes));

        return Rewound(kept);
    }

    private static MemoryStream Rewound(MemoryStream kept)
    {
        kept.Position = 0;

        return kept;
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
