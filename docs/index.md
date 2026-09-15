# Snail.Toolkit.Web

Questions about the web, answered separately, on one shared foundation.

Six packages, each answering one question. The split is the point: **search never returns page content,
scraping never takes a query.** The moment one does the other's job, this is a single package wearing two
names.

## The packages

| Package | Question | In → out |
| --- | --- | --- |
| `Snail.Toolkit.Web` | — | the shared behaviour every engine needs |
| `Snail.Toolkit.Web.Search` | *where is this written about?* | query → **links** |
| `Snail.Toolkit.Web.Scraping` | *what does this page say?* | **link** → content |
| `Snail.Toolkit.Web.Search.Browser` | engines that answer only a real browser | query → links |
| `Snail.Toolkit.Web.Search.Browser.Agent` | engines that answer only their own search box | query → links |
| `Snail.Toolkit.Web.Scraping.Agent` | *what does it say once the form is filled in?* | **errand** → content |

## Which one do I install

`Search` if a plain HTTP request will do — one request per query, nothing to run. `Scraping` to read a page a
browser has to render. The two `.Browser` packages drive a Playwright browser server and only earn their keep
against an engine that refuses anything less; the `.Agent` ones walk a page rather than request it. Each
depends on what it needs, so installing a leaf brings the core with it.

Together they are the foundation for agent tooling — the two capabilities an agent needs are exactly `search`
and `fetch`.

## Nothing found is not the same as nobody answered

Every operation answers with an `Outcome<TValue, TFailure>`: a value, or the reason there is none. The whole
point is that an empty answer explains itself, so the two cases that read identically as an empty list can be
told apart.

```csharp
if (outcome.TryGetValue(out var page))
    Console.WriteLine(page.Title);
else if (outcome.Failure.Kind is FailureKind.Blocked)
    Console.WriteLine($"Refused: {outcome.Failure.Message}");
```

A search where every engine was refused has no results and is not answered — and reacting to it as though the
web simply had nothing on the subject is the mistake this shape exists to prevent.

Start with [Getting started](getting-started.md), or read [the principles](principles.md) the library is
written by.
