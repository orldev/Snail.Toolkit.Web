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
            var response = await _http.For(proxy).GetAsync(address, budget.Token);

            if (!response.IsSuccessStatusCode)
            {
                Log.SitemapAnswered(logger, address, (int)response.StatusCode);

                return SitemapFile.Empty;
            }

            return SitemapFile.Parse(await TextAsync(response, budget.Token));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            Log.SitemapUnreadable(logger, exception, address);

            return SitemapFile.Empty;
        }
    }

    private async Task<string> TextAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var body = await response.Content.ReadAsStreamAsync(ct);

        var raw = await BoundedAsync(body, ct);

        if (raw is not [GzipFirstByte, GzipSecondByte, ..])
            return Encoding.UTF8.GetString(raw);

        await using var compressed = new MemoryStream(raw);
        await using var expanding = new GZipStream(compressed, CompressionMode.Decompress);

        return Encoding.UTF8.GetString(await BoundedAsync(expanding, ct));
    }

    private async Task<byte[]> BoundedAsync(Stream source, CancellationToken ct)
    {
        var kept = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (kept.Length < _options.MaxSitemapBytes)
        {
            var read = await source.ReadAsync(buffer, ct);

            if (read == 0)
                return kept.ToArray();

            await kept.WriteAsync(buffer.AsMemory(0, (int)Math.Min(read, _options.MaxSitemapBytes - kept.Length)), ct);
        }

        Log.SitemapCut(logger, _options.MaxSitemapBytes, CrawlOptions.SectionName, nameof(CrawlOptions.MaxSitemapBytes));

        return kept.ToArray();
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
