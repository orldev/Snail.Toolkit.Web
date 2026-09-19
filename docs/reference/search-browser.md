# Snail.Toolkit.Web.Search.Browser

*Generated from the compiler's documentation by `dotnet run --project gen -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

The public surface of this package: 6 types. Everything else it contains is internal and is described under [Decisions](../decisions/index.md) where it carries a measurement.

## Bing

Asks Bing through a real browser.

The same engine this library already asks over plain HTTP, read the way that works. Measured on 2026-09-15 from a datacentre address, five queries each: over HTTP the engine answered «"Роснано"» with bible sites, «Роснано» with a hotel chain, «Rosnano» with Zurich tourism and «отзыв лицензии КИВИ Банк» with car parts — ten confident results per query, about something else entirely. Through a browser the same address answered «Роснано» with Группа РОСНАНО, the Wikipedia article and the company's own press centre, three times out of three, and «отзыв лицензии КИВИ Банк» with «Отозвана лицензия на осуществление банковских операций у…» and «Что известно об отзыве лицензии у Киви-банка».

THE FIRST READ IN A FRESH CONTEXT IS THE UNRELIABLE ONE. Of three queries measured three times each, two failed on their first attempt — one with an empty page, one with a navigation that never settled — and answered properly on the second and third. That is why the page is taken whole rather than as the document alone: `DocumentOnly` was measured too, and it returned the stale or wrong page where the full load returned the right one.

Every result link is wrapped in the engine's own redirect — `/ck/a?…&u=a1<base64url>` — and unwrapping it costs nothing, unlike Google's `/goto`, which has to be asked where it leads. The address is inside the wrapper, base64url after the two-character marker.

**ProviderName** — The name this provider is registered and configured under.


## BingOptions

Which Bing front end is asked.

**Domain** — Gets or sets the host the query is sent to, without a scheme.

A setting rather than a constant for the same reason Brave's is: the engine runs regional front ends, and which one answers is a deployment decision. Interface language and region are not settings here — they come from the identity the page is fetched under, which is the only way they cannot contradict the user agent beside them.

**SectionName** — The configuration section this binds from.


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

**FindAsync** — *no summary*

Bounded by `Timeout`, the same setting that bounds an engine asked over HTTP. Until 2026-09-20 it bounded only those: it was wired into the HTTP client's pipeline and nowhere else, so a browser engine lasted as long as its navigation timeouts times its retries — a consumer measured 136 to 392 seconds against a one-minute setting. Running out of it is a fault, as it is over HTTP, and a cancellation by the caller stays a cancellation.

**Host** — *no summary*

The same host the engine is read at, said once more where the search can see it: a caller weighing what a query costs asks the gate about this, and an engine driven through a browser is the expensive kind.


## ServiceCollectionExtensions

Registers the search providers that need a real browser.

**AddBing** — Adds Bing, read through a browser instead of over plain HTTP.

TAKES THE PLACE OF THE HTTP PROVIDER OF THE SAME NAME, and that is the point rather than a side effect. Measured on 2026-09-15 from a datacentre address: over HTTP the engine answered four Russian queries out of five with ten confident results about something else — bible sites for «Роснано», a hotel chain, Zurich tourism, car parts — while the same address read through a browser answered with the company, its Wikipedia article and the news about its licence.

Two providers under one name would be worse than either: a report that says «Bing answered nothing» could no longer say which Bing, and the aggregator would spend two of its slots per query on one engine. So the browser reading replaces the plain one wherever this method is called, and a deployment with no browser keeps the plain one by simply not calling it.

**AddBrave** — Adds Brave, read at its own results address.

Registered beside Google rather than instead of it, because which engine answers a given client from a given address is a fact that changes: measured on 2026-09-12 Brave answered and Google did not, and on 2026-09-14 Brave answered `429` from the same address while Google returned six results. The aggregator is where that stops mattering. Each provider costs a page load, so a deployment that wants only one of them adds it on its own instead of taking the set.

Pulls in search and the page reader rather than assuming them, so that saying one thing is enough — the same way `AddGoogle` does in the package next door. Until 2026-09-14 this method registered the provider alone, so a deployment that took the remark above at its word built a container that resolved `Brave` into an `IPageReader` nobody had registered. That failure arrives on the first query rather than at startup, because validation checks settings and not the graph.

**AddBrowserSearch** — Registers search with the browser-backed providers included.

Pulls in both halves deliberately: browser search is HTTP search plus a page reader, and a consumer that asked for it wants the HTTP providers alongside rather than instead. Both registrations are idempotent, so calling them again elsewhere costs nothing.

Google is not here. It answers a form rather than an address — measured on 2026-09-13, its results address returns `/sorry/` where its own form returns results — and walking a form needs the page agent, which this package deliberately does not depend on. It ships in `Snail.Toolkit.Web.Search.Browser.Agent`, and a deployment that wants both adds that one too.
