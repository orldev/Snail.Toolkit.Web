# Snail.Toolkit.Web

Two questions about the web, answered separately, on one shared foundation.

| Package | Question | In → out |
| --- | --- | --- |
| `Snail.Toolkit.Web` | — | the shared behaviour both engines need |
| `Snail.Toolkit.Web.Search` | *where is this written about?* | query → **links** |
| `Snail.Toolkit.Web.Scraping` | *what does this page say?* | **link** → content |
| `Snail.Toolkit.Web.Search.Browser` | search engines that need a real browser | query → links |

The split is the point: **search never returns page content, scraping never takes a query.** The moment
one does the other's job, this is a single package wearing two names.

Together they are the foundation for agent tooling — the two capabilities an agent needs are exactly
`search` and `fetch`.

## The shared core

`Snail.Toolkit.Web` holds what both engines need to behave like a well-mannered, unremarkable web client,
and it knows about neither Playwright nor `HttpClient`:

- **Identity** — user agent, platform, brand, locale, languages and proxy decided as one coherent whole,
  so nothing a site can measure contradicts anything else.
- **Politeness** — a concurrency limit and a minimum gap per host. Request rate is the signal a site reads
  first, and no amount of fingerprinting hides it.
- **Robots** — parsing and matching; *fetching* `robots.txt` belongs to whichever transport is in use.
- **Block detection** — challenge, consent and refusal pages recognised in the body and in the final URL.
- **Text extraction** — readable text with its paragraph structure intact.
- **URL identity** — one normalisation, so the link search deduplicates by is the link scraping throttles by.
- **Outcome** — failure as data, never as an empty string.

## Layout

```
src/Web              → Snail.Toolkit.Web
src/Search           → Snail.Toolkit.Web.Search
src/Scraping         → Snail.Toolkit.Web.Scraping
src/Search.Browser   → Snail.Toolkit.Web.Search.Browser
tests/…
```

Project folders are short so the solution reads well in an IDE. The names that reach a consumer —
`PackageId`, `AssemblyName`, `RootNamespace` — are derived from the folder name in
`Directory.Build.props` and are always fully qualified.

## Dependency rules

1. The core depends on neither Playwright nor `HttpClient`.
2. `Search` does not depend on `Scraping` — that is what `Search.Browser` is for, so that a consumer using
   only HTTP providers never pays for a browser driver.
3. Fetching `robots.txt` belongs to the transport, not to the core.

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

MIT. See [LICENSE](LICENSE).
