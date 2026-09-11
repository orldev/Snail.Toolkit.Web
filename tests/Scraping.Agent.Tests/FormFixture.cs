using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Snail.Toolkit.Web.Scraping.Agent.Tests;

/// <summary>
/// A Playwright browser server and a page that only answers a visitor who actually typed.
/// </summary>
/// <remarks>
/// The page is a miniature of a live registry measured on 2026-09-11: its suggestions hang off a key event, so
/// assigning the field's value leaves the site none the wiser, and its submit button answers "nothing found"
/// unless a suggestion was chosen first. That combination is what makes a walkthrough testable at all — on a
/// form that reacts to anything, a broken agent and a working one produce the same page.
/// </remarks>
public sealed class FormFixture : IAsyncLifetime
{
    /// <summary>The host name the browser resolves the fixture site by.</summary>
    public const string Host = "site";

    /// <summary>The root the fixture pages are served from, as the browser sees it.</summary>
    public const string BaseUrl = $"http://{Host}";

    private const int ServerPort = 3000;

    private readonly INetwork _network = new NetworkBuilder().Build();
    private IContainer? _browser;
    private IContainer? _site;

    /// <summary>The ws:// endpoint of the browser server.</summary>
    public string BrowserServer { get; private set; } = string.Empty;

    private static string Version =>
        typeof(Microsoft.Playwright.Playwright).Assembly.GetName().Version!.ToString(3);

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _network.CreateAsync();

        _browser = new ContainerBuilder($"mcr.microsoft.com/playwright:v{Version}")
            .WithNetwork(_network)
            .WithPortBinding(ServerPort, true)
            .WithCommand("/bin/sh", "-c",
                $"npx -y playwright@{Version} run-server --port {ServerPort} --host 0.0.0.0")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Listening on ws://", strategy => strategy.WithTimeout(TimeSpan.FromMinutes(5))))
            .Build();

        await _browser.StartAsync();

        _site = new ContainerBuilder("nginx:alpine")
            .WithNetwork(_network)
            .WithNetworkAliases(Host)
            .WithPortBinding(80, true)
            .WithResourceMapping(Bytes(Pages.Nginx), "/etc/nginx/conf.d/default.conf")
            .WithResourceMapping(Bytes(Pages.Form), "/usr/share/nginx/html/form.html")
            .WithResourceMapping(Bytes(Pages.Minting), "/usr/share/nginx/html/minting.html")
            .WithResourceMapping(Bytes(Pages.Suggestions), "/usr/share/nginx/html/suggest.json")
            .WithResourceMapping(Bytes(Pages.Robots), "/usr/share/nginx/html/robots.txt")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(80))
            .Build();

        await _site.StartAsync();

        BrowserServer = $"ws://{_browser.Hostname}:{_browser.GetMappedPublicPort(ServerPort)}/";
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

    /// <summary>The documents the fixture site serves.</summary>
    internal static class Pages
    {
        public const string Robots =
            """
            User-agent: *
            Allow: /
            """;

        public const string Suggestions =
            """
            {"items":["Acme Holdings","Acme Trading"]}
            """;

        /// <summary>Echoes the query string back, so a test can see what the page actually sent.</summary>
        /// <remarks>
        /// A static file cannot show whether a minted credential reached the service, and nothing here is going
        /// to validate a real token anyway. Echoing the query string is what makes the claim testable: the
        /// assertion is that the page attached the credential it was told to mint, under the right action.
        /// </remarks>
        public const string Nginx =
            $$"""
            server {
                listen 80;
                server_name {{Host}};
                root /usr/share/nginx/html;
                index index.html;

                location /echo {
                    default_type application/json;
                    return 200 '{"args":"$args"}';
                }
            }
            """;

        /// <summary>A page that issues credentials to itself, the way an invisible captcha does.</summary>
        public const string Minting =
            """
            <!doctype html>
            <html><body><p>service desk</p>
            <script>
              window.grecaptcha = {
                ready: (callback) => callback(),
                execute: (key, options) => Promise.resolve('minted-' + options.action)
              };
            </script>
            </body></html>
            """;

        /// <summary>A search form that notices typing and ignores an assigned value.</summary>
        public const string Form =
            """
            <!doctype html>
            <html><body>
              <input id="party" autocomplete="off" placeholder="name">
              <ul id="hints" hidden></ul>
              <button id="find">Find</button>
              <div id="results"></div>
              <script>
                let chosen = null;
                const party = document.getElementById('party');
                const hints = document.getElementById('hints');

                party.addEventListener('keyup', async () => {
                  const asked = party.value.trim();
                  if (asked.length < 3) { hints.hidden = true; return; }
                  await fetch('/suggest.json?name=' + encodeURIComponent(asked));
                  hints.innerHTML = '<li class="hint">Acme Holdings</li><li class="hint">Acme Trading</li>';
                  hints.hidden = false;
                });

                hints.addEventListener('click', (event) => {
                  if (!event.target.classList.contains('hint')) return;
                  chosen = event.target.textContent;
                  party.value = chosen;
                  hints.hidden = true;
                });

                document.getElementById('find').addEventListener('click', () => {
                  document.getElementById('results').textContent = chosen
                    ? 'Case A40-1/2026 for ' + chosen
                    : 'Nothing found';
                });
              </script>
            </body></html>
            """;
    }
}

/// <summary>Shares one browser server and one site across the walkthrough tests.</summary>
[CollectionDefinition(Name)]
public sealed class FormCollection : ICollectionFixture<FormFixture>
{
    /// <summary>The collection name the tests join.</summary>
    public const string Name = "form";
}
