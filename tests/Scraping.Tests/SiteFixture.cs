using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DotNet.Testcontainers.Builders;
using Snail.Toolkit.Web.Tests.Browsers;
using Snail.Toolkit.Web.Scraping.Browsers;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Snail.Toolkit.Web.Scraping.Tests;

/// <summary>
/// A Playwright browser server and a static site, on one Docker network.
/// </summary>
/// <remarks>
/// The site is reachable from the browser by the alias <see cref="Host"/> because both containers share a
/// network, which is what makes these tests hermetic. The suite this came from navigated to live news sites, so
/// it could not run in CI, could not be trusted when it did, and had every URL commented out.
/// <para>
/// The browser is built with a plain <see cref="ContainerBuilder"/> rather than the Testcontainers Playwright
/// module. The module pins its own image — an older one than the referenced client, which Playwright refuses to
/// talk to — and its start-up command leaves the server listening on <c>ws://[::1]</c>, reachable only from
/// inside the container. Owning the image tag, the command and the wait strategy here is what keeps the server
/// and the client on the same version and the port usable.
/// </para>
/// </remarks>
public sealed class SiteFixture : IAsyncLifetime
{
    /// <summary>The host name the browser resolves the fixture site by.</summary>
    public const string Host = "site";

    private const int ServerPort = 3000;

    private const int SecurePort = 443;

    private readonly INetwork _network = new NetworkBuilder().Build();
    private IContainer? _browser;
    private IContainer? _site;

    /// <summary>The ws:// endpoint of the browser server.</summary>
    public string BrowserServer { get; private set; } = string.Empty;

    /// <summary>The engine that server drives, which the toolkit has to be told about to connect at all.</summary>
    public Engine Engine => BrowserImage.Engine;

    /// <summary>The root the fixture pages are served from, as the test process sees it.</summary>
    public string LocalUrl { get; private set; } = string.Empty;

    /// <summary>The root the fixture pages are served from, as the browser sees it.</summary>
    public const string BaseUrl = $"http://{Host}";

    /// <summary>The same pages over TLS, for anything the platform exposes only in a secure context.</summary>
    /// <remarks>
    /// <c>navigator.userAgentData</c> is the reason this exists: it is undefined outside a secure context, so a
    /// client-hint assertion made over <see cref="BaseUrl"/> would read "absent" and pass without proving
    /// anything. The certificate is self-signed and generated per run, so a test using this root has to open the
    /// reader with <c>IgnoreCertificateErrors</c>.
    /// </remarks>
    public const string SecureBaseUrl = $"https://{Host}";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _network.CreateAsync();

        _browser = new ContainerBuilder(BrowserImage.Name)
            .WithNetwork(_network)
            .WithPortBinding(ServerPort, true)
            .Serving(ServerPort)
            .Build();

        await _browser.StartAsync();

        var (certificate, key) = SelfSigned();

        _site = new ContainerBuilder("nginx:alpine")
            .WithNetwork(_network)
            .WithNetworkAliases(Host)
            .WithPortBinding(80, true)
            .WithPortBinding(SecurePort, true)
            .WithResourceMapping(Bytes(Pages.Nginx), "/etc/nginx/conf.d/default.conf")
            .WithResourceMapping(certificate, "/etc/nginx/site.crt")
            .WithResourceMapping(key, "/etc/nginx/site.key")
            .WithResourceMapping(Bytes(Pages.Article), "/usr/share/nginx/html/article.html")
            .WithResourceMapping(Bytes(Pages.Hub), "/usr/share/nginx/html/hub.html")
            .WithResourceMapping(Bytes(Pages.Grain), "/usr/share/nginx/html/grain.html")
            .WithResourceMapping(Bytes(Pages.Barley), "/usr/share/nginx/html/barley.html")
            .WithResourceMapping(Bytes(Pages.SitemapIndex), "/usr/share/nginx/html/sitemap.xml")
            .WithResourceMapping(Gzipped(Pages.Urlset), "/usr/share/nginx/html/pages.xml.gz")
            .WithResourceMapping(Bytes(Pages.Challenge), "/usr/share/nginx/html/challenge.html")
            .WithResourceMapping(Bytes(Pages.Blank), "/usr/share/nginx/html/blank.html")
            .WithResourceMapping(Bytes(Pages.Probe), "/usr/share/nginx/html/probe.html")
            .WithResourceMapping(Bytes(Pages.Robots), "/usr/share/nginx/html/robots.txt")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilInternalTcpPortIsAvailable(80)
                .UntilInternalTcpPortIsAvailable(SecurePort))
            .Build();

        await _site.StartAsync();

        BrowserServer = $"ws://{_browser.Hostname}:{_browser.GetMappedPublicPort(ServerPort)}/";
        LocalUrl = $"http://{_site.Hostname}:{_site.GetMappedPublicPort(80)}";
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_site is not null)
            await _site.DisposeAsync();

        if (_browser is not null)
            await _browser.DisposeAsync();

        await _network.DisposeAsync();
    }

    private static byte[] Bytes(string content) => Encoding.UTF8.GetBytes(content);

    /// <summary>A throwaway certificate for <see cref="Host"/>, valid for the life of the run.</summary>
    private static (byte[] Certificate, byte[] Key) SelfSigned()
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest($"CN={Host}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();

        names.AddDnsName(Host);
        request.CertificateExtensions.Add(names.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        return (Bytes(certificate.ExportCertificatePem()), Bytes(rsa.ExportPkcs8PrivateKeyPem()));
    }

    /// <summary>A sitemap as sites actually serve it: gzipped, with no Content-Encoding to announce it.</summary>
    private static byte[] Gzipped(string content)
    {
        var compressed = new MemoryStream();

        using (var writer = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            writer.Write(Bytes(content));

        return compressed.ToArray();
    }

    private static class Pages
    {
        public const string Article =
            """
            <!doctype html>
            <html><head><style>p { color: red }</style></head>
            <body>
              <script>window.tracked = true;</script>
              <h1>Grain report</h1>
              <p>Wheat prices rose.</p>
              <p>Barley held steady.</p>
            </body></html>
            """;

        public const string Challenge =
            """
            <!doctype html>
            <html><body><div id="cf-browser-verification">Checking your browser before accessing</div></body></html>
            """;

        public const string Blank = "<!doctype html><html><body></body></html>";

        public const string Robots =
            $"""
            User-agent: *
            Disallow: /private
            Crawl-delay: 0
            Sitemap: {BaseUrl}/sitemap.xml
            """;

        public const string Hub =
            $"""
            <!doctype html>
            <html><body>
              <h1>Everything we publish</h1>
              <a href="/grain.html">grain</a>
              <a href="/barley.html">barley</a>
              <a href="https://elsewhere.example/grain">somewhere else</a>
              <a href="mailto:editor@example.com">write to us</a>
            </body></html>
            """;

        public const string Grain =
            """
            <!doctype html>
            <html><body><h1>Grain</h1><p>Wheat prices rose.</p><a href="/hub.html">back</a></body></html>
            """;

        public const string Barley =
            """
            <!doctype html>
            <html><body><h1>Barley</h1><p>Barley held steady.</p><a href="/grain.html">grain</a></body></html>
            """;

        public const string SitemapIndex =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <sitemap><loc>{BaseUrl}/pages.xml.gz</loc></sitemap>
            </sitemapindex>
            """;

        public const string Urlset =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>{BaseUrl}/grain.html</loc><lastmod>2026-09-01</lastmod></url>
              <url><loc>{BaseUrl}/barley.html</loc></url>
            </urlset>
            """;

        /// <summary>Serves the same document root over both http and TLS.</summary>
        public const string Nginx =
            $$"""
            server {
                listen 80;
                server_name {{Host}};
                root /usr/share/nginx/html;
                index index.html;
            }

            server {
                listen 443 ssl;
                server_name {{Host}};
                ssl_certificate /etc/nginx/site.crt;
                ssl_certificate_key /etc/nginx/site.key;
                root /usr/share/nginx/html;
                index index.html;
            }
            """;

        /// <summary>Reports what a page script can observe, so the stealth layer can be asserted end to end.</summary>
        public const string Probe =
            """
            <!doctype html>
            <html><body><pre id="probe"></pre>
            <script>
              const webgl = () => {
                try {
                  const context = document.createElement('canvas').getContext('webgl');
                  if (!context) return 'unavailable';
                  return String(context.getParameter(context.VERSION)).slice(0, 6);
                } catch (error) {
                  return 'threw:' + error.message;
                }
              };
              const vendor = () => {
                try {
                  const context = document.createElement('canvas').getContext('webgl');
                  return context ? String(context.getParameter(37445)) : 'unavailable';
                } catch (error) {
                  return 'threw:' + error.message;
                }
              };
              const missing = () => isSecureContext ? 'unsupported' : 'insecure-context';
              const brands = () => navigator.userAgentData
                ? navigator.userAgentData.brands.map(entry => entry.brand + ';' + entry.version).join(', ')
                : missing();
              const uaPlatform = () => navigator.userAgentData
                ? navigator.userAgentData.platform
                : missing();
              document.getElementById('probe').textContent = JSON.stringify({
                platform: navigator.platform,
                languages: navigator.languages,
                webdriver: navigator.webdriver === undefined ? 'undefined' : String(navigator.webdriver),
                hardwareConcurrency: navigator.hardwareConcurrency,
                webgl: webgl(),
                vendor: vendor(),
                brands: brands(),
                uaPlatform: uaPlatform()
              });
            </script>
            </body></html>
            """;
    }
}

/// <summary>Shares one browser server and one site across the integration tests.</summary>
[CollectionDefinition(Name)]
public sealed class SiteCollection : ICollectionFixture<SiteFixture>
{
    /// <summary>The collection name integration tests join.</summary>
    public const string Name = "site";
}
