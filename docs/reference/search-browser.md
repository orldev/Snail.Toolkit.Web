# Snail.Toolkit.Web.Search.Browser

*Generated from the compiler's documentation by `dotnet run --project gen -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

The public surface of this package: 4 types. Everything else it contains is internal and is described under [Decisions](../decisions/index.md) where it carries a measurement.

## Brave

Asks Brave through a real browser.

Measured against nine engines on 2026-09-12 from a datacentre address: this one answered where the others would not. Google returned a captcha both by address and through its own form, Ecosia refused outright, Yandex answered once and then demanded a captcha on the next two attempts, and Mojeek and Startpage named nothing at all. Brave returned a hundred links across twenty-six sites and did it without wrapping them — Bing and the DuckDuckGo endpoint both hide their results behind a redirect of their own, which has to be unwrapped before a link is a link.

That is the whole reason this class is short: what differs per engine is the address a query becomes and the markup it comes back as, and here neither needs any unwrapping.

It also counts requests rather than pages, and for a while that looked like a refusal. On 2026-09-14 the address it had answered with a hundred links was met with `429` on every attempt, and the signature looked like the cause: a Gecko user agent was answered twice out of four while Chrome, Safari and an unsigned client were refused outright. The real cause was cheaper and entirely ours. One ordinary page load here costs 71 to 73 requests, and the budget was being spent inside it: measured with the arms alternating and a fresh context each, the full page was refused three times out of three while the document alone was answered three times out of three, carrying all eighteen results. Hence `DocumentOnly` below — not a way around the engine, but the amount of it we actually needed.

**ProviderName** — The name this provider is registered and configured under.


## BraveOptions

Which Brave front end is asked.

**Domain** — Gets or sets the host the query is sent to, without a scheme.

A setting rather than a constant for the same reason Google's is: the engine runs more than one front end, and which one answers is a deployment decision. Interface language and region are not settings here — they come from the identity the page is fetched under, which is the only way they cannot contradict the user agent beside them.

**SectionName** — The configuration section this binds from.


## BrowserSearch

A search engine that only answers a real browser.

Google is not alone in this: Bing, Yandex, Brave and Baidu all meet a plain HTTP client with a consent wall or a challenge, and every one of them then needs the same handling — read the page with a browser, tell a refusal from a page that merely could not be read, drop the links that are the page's own furniture, and say so when a page that loaded fine parsed into nothing. Only two things differ per engine: the address a query becomes, and the markup it comes back as. Those are what a subclass supplies.

How the page is obtained is left to the engine, because the two ways differ in more than an address. An engine that answers a link is read through `IPageReader`; one that answers only a form filled in by hand is walked through by the page agent. Measured on 2026-09-13, that is not a preference: asked at its results address Google answers `/sorry/` and asked through its own form it returns results, with a passing control on either side of the refusal. Either way the engine inherits the per-host gate, the coherent identity, the stealth script, challenge detection and the retry policy instead of restating them.

An engine is a type of its own rather than a configured instance because that is what lets it be registered with `AddSearchProvider<T>()` and stay idempotent: a factory registration could not be deduplicated and a second call would ask the same engine twice per query.


## ServiceCollectionExtensions

Registers the search providers that need a real browser.

**AddBrave** — Adds Brave, read at its own results address.

Registered beside Google rather than instead of it, because which engine answers a given client from a given address is a fact that changes: measured on 2026-09-12 Brave answered and Google did not, and on 2026-09-14 Brave answered `429` from the same address while Google returned six results. The aggregator is where that stops mattering. Each provider costs a page load, so a deployment that wants only one of them adds it on its own instead of taking the set.

Pulls in search and the page reader rather than assuming them, so that saying one thing is enough — the same way `AddGoogle` does in the package next door. Until 2026-09-14 this method registered the provider alone, so a deployment that took the remark above at its word built a container that resolved `Brave` into an `IPageReader` nobody had registered. That failure arrives on the first query rather than at startup, because validation checks settings and not the graph.

**AddBrowserSearch** — Registers search with the browser-backed providers included.

Pulls in both halves deliberately: browser search is HTTP search plus a page reader, and a consumer that asked for it wants the HTTP providers alongside rather than instead. Both registrations are idempotent, so calling them again elsewhere costs nothing.

Google is not here. It answers a form rather than an address — measured on 2026-09-13, its results address returns `/sorry/` where its own form returns results — and walking a form needs the page agent, which this package deliberately does not depend on. It ships in `Snail.Toolkit.Web.Search.Browser.Agent`, and a deployment that wants both adds that one too.
