using System.Text;
using DotNet.Testcontainers.Builders;
using Snail.Toolkit.Web.Tests.Browsers;
using Snail.Toolkit.Web.Scraping.Browsers;
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

    /// <summary>The engine that server drives, which the toolkit has to be told about to connect at all.</summary>
    public Engine Engine => BrowserImage.Engine;

    private static string Version =>
        typeof(Microsoft.Playwright.Playwright).Assembly.GetName().Version!.ToString(3);

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

        _site = new ContainerBuilder("nginx:alpine")
            .WithNetwork(_network)
            .WithNetworkAliases(Host)
            .WithPortBinding(80, true)
            .WithResourceMapping(Bytes(Pages.Nginx), "/etc/nginx/conf.d/default.conf")
            .WithResourceMapping(Bytes(Pages.Form), "/usr/share/nginx/html/form.html")
            .WithResourceMapping(Bytes(Pages.Captioned), "/usr/share/nginx/html/captioned.html")
            .WithResourceMapping(Bytes(Pages.Guarded), "/usr/share/nginx/html/guarded.html")
            .WithResourceMapping(Bytes(Pages.Keyed), "/usr/share/nginx/html/keyed.html")
            .WithResourceMapping(Bytes(Pages.Widgeted), "/usr/share/nginx/html/widgeted.html")
            .WithResourceMapping(Bytes(Pages.Challenge), "/usr/share/nginx/html/challenge.html")
            .WithResourceMapping(Bytes(Pages.Turning), "/usr/share/nginx/html/turning.html")
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

                location /flaky {
                    default_type application/json;
                    return 503 '{"error":"busy"}';
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

        /// <summary>A form with no button at all, and a real list that has to be chosen from.</summary>
        /// <remarks>
        /// Both halves copy shapes measured on live search engines on 2026-09-12, and both are built so that
        /// the move under test cannot pass by accident. There is no submitting element anywhere, so a
        /// walkthrough that cannot press a key cannot finish this form — which is exactly what happened on two
        /// engines whose only submit is the return key. And the region is a real <c>select</c>, drawn by the
        /// browser outside the page: the form answers a refusal until one is chosen, so a step that merely
        /// clicked at an option would be caught by the answer rather than pass silently.
        /// </remarks>
        public const string Keyed =
            """
            <!doctype html>
            <html><head><title>Ledger</title></head><body>
              <form id="lookup">
                <label for="what">Ledger number</label>
                <input id="what" name="what" autocomplete="off">
                <select id="where" name="where">
                  <option value="">choose a region</option>
                  <option value="n">Northern</option>
                  <option value="s">Southern</option>
                </select>
              </form>
              <div id="answer"></div>
              <script>
                document.getElementById('lookup').addEventListener('submit', (event) => {
                  event.preventDefault();
                  const what = document.getElementById('what').value.trim();
                  const where = document.getElementById('where');
                  document.getElementById('answer').textContent = where.value
                    ? 'Ledger ' + what + ' is held in ' + where.options[where.selectedIndex].text
                    : 'Choose a region first';
                });
              </script>
            </body></html>
            """;

        /// <summary>A form whose list is hidden behind a widget of the site's own drawing.</summary>
        /// <remarks>
        /// Copies the German register portal as measured on 2026-09-12: the native <c>select</c> sits in a
        /// container clipped to one pixel, the way PrimeFaces and Select2 keep it, and the site draws a label
        /// and a panel of its own over it. The form submits the native one all the same. The answer names the
        /// option's text, so a step that set the value on the wrong element, or on none, is caught by the answer.
        /// </remarks>
        public const string Widgeted =
            """
            <!doctype html>
            <html><head><title>Register</title>
            <style>.hidden-accessible{position:absolute;clip:rect(1px,1px,1px,1px);width:1px;height:1px;overflow:hidden}</style>
            </head><body>
              <form id="lookup">
                <input id="what" name="what" autocomplete="off">
                <div class="menu">
                  <div class="hidden-accessible">
                    <select id="where_input" name="where_input" tabindex="-1" aria-hidden="true">
                      <option value="">choose a court</option>
                      <option value="m">Munich</option>
                      <option value="b">Berlin</option>
                    </select>
                  </div>
                  <label id="where_label">choose a court</label>
                </div>
                <button id="find">Find</button>
              </form>
              <div id="answer"></div>
              <script>
                document.getElementById('lookup').addEventListener('submit', (event) => {
                  event.preventDefault();
                  const where = document.getElementById('where_input');
                  document.getElementById('answer').textContent = where.value
                    ? 'Entry ' + document.getElementById('what').value.trim() + ' at ' + where.options[where.selectedIndex].text
                    : 'Choose a court first';
                });
              </script>
            </body></html>
            """;

        /// <summary>A form that accepts the move and then shows the door.</summary>
        /// <remarks>
        /// Copies what a search engine did on 2026-09-12: it took the submit, began loading the results and
        /// then redirected to a challenge. Every step of the errand succeeds here, which is the point — the
        /// refusal is invisible to anything that only watches whether the moves landed.
        /// </remarks>
        public const string Guarded =
            """
            <!doctype html>
            <html><head><title>Look up</title></head><body>
              <input id="what" autocomplete="off">
              <button id="go">Go</button>
              <script>
                document.getElementById('go').addEventListener('click', () => {
                  window.location = '/challenge.html';
                });
              </script>
            </body></html>
            """;

        /// <summary>The page it ends on, wearing a marker the detector knows.</summary>
        public const string Challenge =
            """
            <!doctype html>
            <html><head><title>Just a moment</title></head>
            <body><div id="cf-browser-verification">Checking your browser before accessing</div></body></html>
            """;

        /// <summary>A page that welcomes a visitor and shows them the door the moment they use it.</summary>
        /// <remarks>
        /// A miniature of what Google does, measured on 2026-09-13: the home page loads, the query is typed,
        /// the form submit is accepted, the results begin to load — and then the site redirects to a challenge.
        /// A fixture that refuses on arrival cannot reproduce it, because the arrival is already guarded.
        /// </remarks>
        public const string Turning =
            """
            <!doctype html>
            <html><head><title>Search</title></head>
            <body>
              <form action="/challenge.html" method="get">
                <label for="q">Search</label>
                <input id="q" name="q" type="text">
                <button id="go" type="submit">Go</button>
              </form>
            </body></html>
            """;

        /// <summary>A form whose fields are named by captions rather than by anything bound to them.</summary>
        /// <remarks>
        /// Both rows copy shapes measured on a live registry on 2026-09-12. The first is its own: a
        /// <c>label</c> with no <c>for</c> attribute and no field inside it, the field sitting in a sibling
        /// container — <c>GetByLabel</c> matched nothing there, while the first field following the caption
        /// matched exactly one. The second drops the <c>label</c> element altogether, which no label engine
        /// can resolve, so the fallback cannot pass by accident.
        /// <para>
        /// The placeholders are written in by script for the reason they were on the live page that cost
        /// three runs: a placeholder is not in the markup the server sent, so it is there on one load and
        /// gone on the next, and a walkthrough that addresses one is addressing something that may not exist.
        /// </para>
        /// </remarks>
        public const string Captioned =
            """
            <!doctype html>
            <html><head><title>Notices</title></head><body>
              <form class="wr-form">
                <div class="form-row">
                  <div class="label-panel"><label> Notice number <i class="req"></i></label></div>
                  <div class="field-panel"><input class="notice" autocomplete="off"></div>
                </div>
                <div class="form-row">
                  <div class="label-panel"><div class="caption">Vehicle code</div></div>
                  <div class="field-panel"><input class="vehicle" autocomplete="off"></div>
                </div>
                <button id="look">Look up</button>
                <div id="answer"></div>
              </form>
              <script>
                const notice = document.querySelector('.notice');
                const vehicle = document.querySelector('.vehicle');
                notice.placeholder = 'number, name or code';
                vehicle.placeholder = 'VIN or chassis';
                document.getElementById('look').addEventListener('click', (event) => {
                  event.preventDefault();
                  document.getElementById('answer').textContent = notice.value && vehicle.value
                    ? 'Notice ' + notice.value + ' covers ' + vehicle.value
                    : 'Nothing found';
                });
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
