# Changelog

What changed, for someone who has this library in their project and wants to know whether an update will
move the ground under them.

Two things worth knowing about how this file is kept. **Nothing has been released yet**, so there is one
section and it has no date: version `0.1.0` is what the packages carry, and the first published version is
still ahead. And a line here is written from the consumer's side — what is different for code that calls
this library — not from the commit's side. The commit history is where the reasoning lives; this is the
short answer.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions will follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) from the first release onward.

## Unreleased

Everything below is the first release taking shape. The dated groups are there because the last of them
was a single round of work — an audit and its treatment — and it is the part most likely to matter to
someone reading this before the first publication.

### The performance audit's fixes, 2026-10-13

Measured before and after with the benchmarks in `benchmarks/`; every answer is the same as before.

#### Added

- **Text, title, links and canonical address from a document already parsed.** `TextExtraction.Clean` and
  `Title`, `LinkExtraction.From` and `CanonicalOf` each gain an overload taking the `HtmlDocument` that
  `Markup.Read` answers, so a caller reading its own pages parses each once. `Clean` no longer removes
  anything from the document it walks.

#### Changed

- **A page costs one parse.** A page read parses its markup once for text and title (6.44 → 3.66 MB in the
  benchmark), a crawled page once for text, title, links and canonical address (11.67 → 4.22 MB), and a
  browser engine's results page once, without the text nobody read (9.15 → 3.2 MB). `Link.Of` no longer
  builds a `UriBuilder` (374 → 171 KB over a page's links), and `BlockDetector` answers a clear page in one
  pass (0.17–0.21 of the time).
- **A DuckDuckGo results page costs one parse** (1.55 → 1.0 MB a query).
- **What every attempt pays first is a third of what it was** (2 437 → 328 bytes): `IdentityRotation` shapes
  each fingerprint's identity once and hands it out again, and `SiteRules` answers at once from rules it has
  already read. Two identities handed out from one fingerprint now share the values they carry, so they
  compare equal where they used to differ only by the list of languages each held.

### Found by its first MCP consumer, 2026-10-04

Defects that only showed once a model, rather than a program, was reading what the toolkit returned —
each measured through Snail.MCP.Playwright against live pages before it was touched.

#### Added

- **A picture of a page.** `IPageSnapshots.TakeAsync` answers with a `PageSnapshot` — a JPEG of the screen a
  visitor sees, or of the whole page down to `PageSnapshots.TallestPage` pixels — and says in `IsCut`
  whether the page ran on below it. It is registered by `AddScraping` and is a visit like the reader's:
  the same identity, the same politeness, the same judgement of a wall.

#### Fixed

- **Every page of Wikipedia was reported `Blocked`.** The bare word `hcaptcha` stood among the markup
  signals, and MediaWiki names the captcha it would show an editor in the settings it inlines into every
  document. What matches now is the widget — `h-captcha`, and not the hidden `h-captcha-response` field —
  the same way reCAPTCHA has been matched since 2026-09-12. A caller that branched on the signal text sees
  `markup:h-captcha` where it used to see `markup:hcaptcha`.
- **A line break in the source became a line break in the text.** A sentence written across three lines
  with a link in the middle came back as three lines, and a navigation bar as a column of single words.
  Whitespace inside a text node is one space now, and what ends a line is a block element — on both of its
  sides, so text standing directly before a block no longer runs into it. Preformatted text keeps its
  lines. **The text of a page changes**: fewer and longer lines, and a consumer that stored a hash of the
  text will see every page as new once.
- **The title of a page was the first words of its text**, run into the first paragraph because a title is
  not a block. The head is left out of the text; `Title` is where the name is.
- **The cells of a table row ran together.** They stand a space apart.

### Audit round, 2026-09-20

Ten defects found by reading the code, three of them confirmed against live sites, and each fix verified
by the thing that exposed it rather than by a second reading.

#### Fixed

- **A search asked engines it had already been refused by.** The rest a refusing engine was put on was
  checked once per batch instead of per engine-and-query pair, so one refusal cost every remaining query
  a request that was certain to fail.
- **A refusal arriving during a rest doubled that rest.** A single wall could therefore grow the rest to
  its ceiling through nothing but the requests already in flight; it now returns the time still left.
- **A caller who stopped reading results left attempts running.** Cancellation is linked now, so
  abandoning the sequence stops the work behind it.
- **A long `Retry-After` broke the politeness gate** — a site answering with hours of it could push a
  host past what a timer accepts, and the wait was silent. Long waits are chunked, and one reading of
  `Retry-After` now serves the reader, the agent and the site call alike.
- **A browser engine repeated a wall** and could run past `SearchOptions.Timeout`.
- **Losing the browser server was reported as the site's fault.** It is now `BrowserUnavailable`, which
  is the difference between retrying the page and fixing the deployment.
- **A failed Playwright start was remembered forever.** The next call starts the driver again.
- **A forgetting map measured its window from the moment it was written**, so an entry in constant use
  was dropped mid-use. The window runs from the last time it was asked for.
- **DuckDuckGo's result links were read without de-entitizing** `href`, which made the engine itself
  look like the source of a find.
- **Google's two-minute gap** between what it shows and what it serves is named and logged rather than
  silently producing fewer results.

#### Changed

- **The HTTP search clients speak HTTP/2.** This is the largest single finding of the round: over
  HTTP/1.1 DuckDuckGo answered a challenge page, and the engine looked broken. Over HTTP/2 it answers.
- **`TimeProvider` is the one clock** the politeness gate, the forgetting maps and `robots.txt` read, so
  a test no longer waits for real seconds to check a rule about time.
- **"Is this an answer to the query?" is one policy**, shared by every engine, with a threshold taken
  from a measurement rather than chosen.

#### Added

- **A site's permission survives the visit that earned it.** Cookies and local storage are kept per host
  — a cleared Cloudflare challenge, a consent wall's answer, a session — and handed back to the next
  visit to that host, and to no other host. In memory only: a restarted process is a visitor who cleared
  their cookies.
- **A host sees the same guest each time.** Identity and proxy are now chosen per host rather than per
  request, so one site is not visited by a different browser every page.
- **Refusals can be retried on request.** `ScrapingOptions.RepeatsRefusals` — off by default, which is
  the behaviour that was there before.

#### Removed

- **The HTTP Bing provider.** Measured again over HTTP/2 before removing it and it still answers Russian
  queries with pages about something else entirely. Bing through a browser is unaffected.

### Before that

The shape of the library itself: six packages around one core, an `Outcome` that carries failure as data
rather than as an exception, a politeness gate per host, engines that answer only to a real browser and
engines that answer only to their own search box, crawling with a budget, and a browser image built from
the Playwright tag the client is paired with.

### Packaging

- Symbols ship as a `.snupkg` beside each package, with untracked sources embedded.
- The house style is written down in `.editorconfig`, and the part of it the build enforces is marked
  there as exactly that.

### The repository itself

Nothing here changes a line of the published API; it changes what it is like to work in the repository,
which is the other thing a first release fixes in place.

- **The documentation is one folder.** `docs/` now holds the prose, the theme, the generator and the
  MkDocs configuration, where those used to be four separate entries in the root.
- **The build writes to `artifacts/`** instead of a `bin/` and an `obj/` inside every project, so a search
  through a source folder returns sources.
- **The SDK version is pinned in `global.json`.** The build fails on a handful of code style rules, those
  rules ship with the SDK, and a floating SDK therefore meant a floating definition of a green build.
