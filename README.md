# Snail.Toolkit.Web

Questions about the web, answered separately, on one shared foundation.

| Package | Question | In → out |
| --- | --- | --- |
| `Snail.Toolkit.Web` | — | the shared behaviour every engine needs |
| `Snail.Toolkit.Web.Search` | *where is this written about?* | query → **links** |
| `Snail.Toolkit.Web.Scraping` | *what does this page say?* | **link** → content |
| `Snail.Toolkit.Web.Search.Browser` | engines that answer only a real browser | query → links |
| `Snail.Toolkit.Web.Search.Browser.Agent` | engines that answer only their own search box | query → links |
| `Snail.Toolkit.Web.Scraping.Agent` | *what does it say once the form is filled in?* | **errand** → content |

**Which one do I install?** `Search` if a plain HTTP request will do — it is one request per query and needs
nothing running. `Scraping` to read a page a browser has to render. The two `.Browser` packages drive a
Playwright browser server and only earn their keep against an engine that refuses anything less; the
`.Agent` ones walk a page rather than request it. Each depends on what it needs, so installing a leaf
brings the core with it.

The split is the point: **search never returns page content, scraping never takes a query.** The moment
one does the other's job, this is a single package wearing two names.

Together they are the foundation for agent tooling — the two capabilities an agent needs are exactly
`search` and `fetch`.

## Getting started

Both engines are registered the way anything else in this stack is, and read their settings from the
`Web:` section of your configuration.

```csharp
builder.Services.AddSearch(builder.Configuration);      // Snail.Toolkit.Web.Search
builder.Services.AddScraping(builder.Configuration);    // Snail.Toolkit.Web.Scraping
```

```jsonc
{
  "Web": {
    "Politeness": { "MinDelayPerHost": "00:00:01", "MaxConcurrentPerHost": 1 },
    "Scraping": { "BrowserServer": "ws://localhost:3000/" }
  }
}
```

Settings are read once, when the host starts. Every option class is bound and snapshotted at that
point, so a change to `appsettings.json` reaches a running process only when it is restarted — there is
no live reload anywhere in the toolkit, and a setting that appears to have been ignored has usually been
edited under a process that read it an hour ago.

Asking a question, and reading a page:

```csharp
var found = await search.FindAsync(["postgresql documentation"]);

foreach (var result in found.Results)
    Console.WriteLine($"{result.Title} — {result.Address}");
```

```csharp
var outcome = await pages.ReadAsync(Link.Of("https://www.postgresql.org/docs/"));

if (outcome.IsSuccess)
    Console.WriteLine(outcome.Value.Text);
```

### Nothing found is not the same as nobody answered

Every operation answers with an `Outcome<TValue, TFailure>`: a value, or the reason there is none. The
whole point is that an empty answer explains itself, so the two cases that read identically as an empty
list can be told apart.

```csharp
if (outcome.TryGetValue(out var page))
    Console.WriteLine(page.Title);
else if (outcome.Failure.Kind is FailureKind.Blocked)
    Console.WriteLine($"Refused: {outcome.Failure.Message}");
```

`SearchOutcome` answers the same question for a whole search: `HasResults` says whether anything matched,
and `IsAnswered` whether any provider answered at all. A search where every engine was refused has no
results and is not answered — and reacting to it as though the web simply had nothing on the subject is
the mistake this shape exists to prevent.

## The shared core

`Snail.Toolkit.Web` holds what both engines need to behave like a well-mannered, unremarkable web client,
and it knows about neither Playwright nor `HttpClient`:

- **Identity** — user agent, platform, brand, locale, languages and proxy decided as one coherent whole,
  so nothing a site can measure contradicts anything else.
- **Politeness** — a concurrency limit and a minimum gap per host, and the rest a site asks for in
  `Retry-After`, which binds every later caller rather than the one that was told. A gap longer than
  `Web:Politeness:LongestRest` is handed back at once instead of waited out: a caller told to come back in
  thirty seconds is better served by hearing so than by spending its whole budget to be refused again.
  Request rate is the signal a site reads first, and no amount of fingerprinting hides it.
- **Robots** — parsing and matching; *fetching* `robots.txt` belongs to whichever transport is in use.
- **Block detection** — challenge, consent and refusal pages recognised in the body and in the final URL.
- **Text extraction** — readable text with its paragraph structure intact. Markup nested more than a thousand
  tags deep is refused rather than parsed, and the read reports `FailureKind.Unreadable`: parsing is quadratic
  in depth, so a page of 161 KB nested five thousand deep costs four hundred milliseconds of pure CPU where a
  catalogue of 2.3 MB costs a quarter of that. Size is the wrong axis and there is no ceiling on it.
- **URL identity** — one normalisation, so the link search deduplicates by is the link scraping throttles by.
- **Outcome** — failure as data, never as an empty string.

## Walking a page

Some pages say nothing until someone fills a form in. `Snail.Toolkit.Web.Scraping.Agent` takes an
**errand** — an address and the moves to make on it — and returns the page that resulted, or names the move
that produced nothing.

```csharp
var outcome = await agent.PerformAsync(new Errand(Link.Of("https://example.test/search"),
[
    new Step(StepKind.Type, Spot.Labelled("Notice number"))
    {
        Value = "2024-001",
        Expected = new Effect(EffectKind.Request, "suggest.json")
    },
    new Step(StepKind.Click, Spot.Css("#look"))
    {
        Expected = new Effect(EffectKind.Text, "is registered")
    }
]));
```

Three things about it are deliberate, and each came from a measurement rather than a preference:

- **A step counts when the site reacted, not when the move was made.** Assigning a field's value sets it and
  raises none of the events a suggestion list hangs off, so an agent that reports its own movements cannot be
  told apart from a broken one. Every step may name the `Effect` it is held to.
- **How an element is addressed is stated, never inferred.** `Spot.Css` matches a selector; `Spot.Labelled`
  finds the field a visible caption names, falling back from the platform's label engine to the first field
  after the caption — measured necessary on a registry whose caption is bound to its field by position alone.
  There is no implicit conversion from a string, because which of the two is in use is exactly the fact worth
  being explicit about.
- **A fruitless step explains itself.** The failure names the step and then describes the page it failed on:
  how many elements the address matched, how many were visible, what fields were offered instead, and what
  the page asked the site for while the step ran. A page that asked for nothing never noticed the move.

`ISiteCalls` is the other half: it opens a page and lets that page call its own service, so the origin,
cookies and any token the page issues to itself travel with the request. The answer is the service's body,
kept apart from `PageContent` so a payload is never dressed up as text somebody read.

## Layout

```
src/Web                  → Snail.Toolkit.Web
src/Search               → Snail.Toolkit.Web.Search
src/Scraping             → Snail.Toolkit.Web.Scraping
src/Search.Browser       → Snail.Toolkit.Web.Search.Browser
src/Search.Browser.Agent → Snail.Toolkit.Web.Search.Browser.Agent
src/Scraping.Agent       → Snail.Toolkit.Web.Scraping.Agent
tests/…
probes/…
```

Project folders are short so the solution reads well in an IDE. The names that reach a consumer —
`PackageId`, `AssemblyName`, `RootNamespace` — are derived from the folder name in
`Directory.Build.props` and are always fully qualified.

## Dependency rules

1. The core depends on neither Playwright nor `HttpClient` — with one deliberate exception. `Proxied` turns
   a configured exit into a `SocketsHttpHandler`, because every transport needs that and none of them owns
   it, and it costs nothing: the type ships with the framework, so the core still carries no package
   reference for it. Without it a consumer runs with two exits at once — a proxied browser beside plain
   clients going out directly.
2. `Search` does not depend on `Scraping` — that is what `Search.Browser` is for, so that a consumer using
   only HTTP providers never pays for a browser driver.
3. Fetching `robots.txt` belongs to the transport, not to the core.
4. `Scraping.Agent` drives pages through `Scraping`'s own `IVisits` and `IAttempts<T>` rather than arranging a
   visit of its own. Setting an identity in two places is what once left a context announcing one browser in
   its headers and another in script; writing the attempt out by hand in three places is what left the agent
   without the retry the page reader had carried from the beginning.

## Versioning

Every package in the repository carries one version, set in `Directory.Build.props`. Independent versions
only ever apply to leaves: packing turns a `ProjectReference` into a versioned dependency, so a change in
the core forces the dependants to be republished regardless.

Package versions are pinned centrally in `Directory.Packages.props`.

## Building

```bash
dotnet build
dotnet test --filter "Category!=Integration"   # no Docker needed
dotnet test                                    # adds the integration suites
```

## License

MIT. See [LICENSE](https://github.com/orldev/Snail.Toolkit.Web/blob/main/LICENSE).
