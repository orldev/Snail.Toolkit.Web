# Architecture

A layered multi-package library with the dependency on transport inverted — close to hexagonal in shape,
without a domain in the DDD sense. The core declares the ports; the transport packages supply the adapters.

## The graph

```
                    Web  (knows neither Playwright nor HttpClient)
                   ╱   ╲
             Search     Scraping
                │      ╱        ╲
       Search.Browser            Scraping.Agent
                │                      │
        Search.Browser.Agent ──────────┘
```

No cycles, in either the project graph or the namespaces.

## Dependency rules

**The core depends on neither Playwright nor `HttpClient`** — with one deliberate exception. `Proxied` turns a
configured exit into a `SocketsHttpHandler`, because every transport needs that and none of them owns it, and
it costs nothing: the type ships with the framework, so the core still carries no package reference for it.
Without it a consumer runs with two exits at once — a proxied browser beside plain clients going out directly.

**`Search` does not depend on `Scraping`** — that is what `Search.Browser` is for, so that a consumer using
only HTTP providers never pays for a browser driver.

**Fetching `robots.txt` belongs to the transport, not to the core.** Parsing and matching are the core's;
downloading is whichever transport is in use.

**`Scraping.Agent` drives pages through `Scraping`'s own `IVisits` and `IAttempts<T>`** rather than arranging
a visit of its own. Setting an identity in two places is what once left a context announcing one browser in
its headers and another in script; writing the attempt out by hand in three places is what left the agent
without the retry the page reader had carried from the beginning.

## The shared core

`Snail.Toolkit.Web` holds what both engines need to behave like a well-mannered, unremarkable web client.

**Identity** — user agent, platform, brand, locale, languages and proxy decided as one coherent whole, so
nothing a site can measure contradicts anything else.

**Politeness** — a concurrency limit and a minimum gap per host, and the rest a site asks for in `Retry-After`,
which binds every later caller rather than the one that was told. Request rate is the signal a site reads
first, and no amount of fingerprinting hides it.

**Robots** — parsing and matching, with a ceiling on the pace a site may ask for. A `Crawl-delay` is a pace
rather than a refusal, so an unreasonable one is clamped instead of being answered with a failure: refusing
would drop the site from the crawl where clamping only reads it slowly.

**Text extraction** — readable text with its paragraph structure intact. Markup nested more than a thousand
tags deep is refused rather than parsed, and the read reports `FailureKind.Unreadable`: parsing is quadratic in
depth, so a page of 161 KB nested five thousand deep costs four hundred milliseconds of pure CPU where a
catalogue of 2.3 MB costs a quarter of that. Size is the wrong axis and there is no ceiling on it.

**Block detection** — challenge, consent and refusal pages recognised in the body and in the final URL.

**URL identity** — one normalisation, so the link search deduplicates by is the link scraping throttles by.

**Outcome** — failure as data, never as an empty string.

## One version for every package

Independent versions only ever apply to leaves: packing turns a `ProjectReference` into a versioned
dependency, so a change in the core forces the dependants to be republished regardless. Package versions are
pinned centrally, which is what makes two packages of one toolkit drifting apart structurally impossible.

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

Project folders are short so the solution reads well in an IDE. The names that reach a consumer — `PackageId`,
`AssemblyName`, `RootNamespace` — are derived from the folder name and are always fully qualified: leaving
them to their defaults would ship `Search.dll` with a root namespace of `Search`, which collides with
unrelated packages in a consumer's graph and is painful to diagnose.

`probes/` is not a test project. It holds measurements, invoked by name, whose job is to answer a question
before a number is chosen — `dotnet run --project probes -- markup` reports what parsing a page costs, and the
ceiling above came out of it.
