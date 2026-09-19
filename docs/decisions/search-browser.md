# Snail.Toolkit.Web.Search.Browser

*Generated from the compiler's documentation by `dotnet run --project gen -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

12 decisions recorded in this package, each naming the day it was measured. Declarations a consumer cannot see are marked *internal*: they are here because the reasoning is, not because the type is reachable.

## Brave

`2026-09-12`, `2026-09-14`

*Asks Brave through a real browser.*

Measured against nine engines on 2026-09-12 from a datacentre address: this one answered where the others would not. Google returned a captcha both by address and through its own form, Ecosia refused outright, Yandex answered once and then demanded a captcha on the next two attempts, and Mojeek and Startpage named nothing at all. Brave returned a hundred links across twenty-six sites and did it without wrapping them — Bing and the DuckDuckGo endpoint both hide their results behind a redirect of their own, which has to be unwrapped before a link is a link.

That is the whole reason this class is short: what differs per engine is the address a query becomes and the markup it comes back as, and here neither needs any unwrapping.

It also counts requests rather than pages, and for a while that looked like a refusal. On 2026-09-14 the address it had answered with a hundred links was met with `429` on every attempt, and the signature looked like the cause: a Gecko user agent was answered twice out of four while Chrome, Safari and an unsigned client were refused outright. The real cause was cheaper and entirely ours. One ordinary page load here costs 71 to 73 requests, and the budget was being spent inside it: measured with the arms alternating and a fresh context each, the full page was refused three times out of three while the document alone was answered three times out of three, carrying all eighteen results. Hence `DocumentOnly` below — not a way around the engine, but the amount of it we actually needed.


## ServiceCollectionExtensions.AddBrave

`2026-09-12`, `2026-09-14`

*Adds Brave, read at its own results address.*

Registered beside Google rather than instead of it, because which engine answers a given client from a given address is a fact that changes: measured on 2026-09-12 Brave answered and Google did not, and on 2026-09-14 Brave answered `429` from the same address while Google returned six results. The aggregator is where that stops mattering. Each provider costs a page load, so a deployment that wants only one of them adds it on its own instead of taking the set.

Pulls in search and the page reader rather than assuming them, so that saying one thing is enough — the same way `AddGoogle` does in the package next door. Until 2026-09-14 this method registered the provider alone, so a deployment that took the remark above at its word built a container that resolved `Brave` into an `IPageReader` nobody had registered. That failure arrives on the first query rather than at startup, because validation checks settings and not the graph.


## BrowserSearch

`2026-09-13`

*A search engine that only answers a real browser.*

Google is not alone in this: Bing, Yandex, Brave and Baidu all meet a plain HTTP client with a consent wall or a challenge, and every one of them then needs the same handling — read the page with a browser, tell a refusal from a page that merely could not be read, drop the links that are the page's own furniture, and say so when a page that loaded fine parsed into nothing. Only two things differ per engine: the address a query becomes, and the markup it comes back as. Those are what a subclass supplies.

How the page is obtained is left to the engine, because the two ways differ in more than an address. An engine that answers a link is read through `IPageReader`; one that answers only a form filled in by hand is walked through by the page agent. Measured on 2026-09-13, that is not a preference: asked at its results address Google answers `/sorry/` and asked through its own form it returns results, with a passing control on either side of the refusal. Either way the engine inherits the per-host gate, the coherent identity, the stealth script, challenge detection and the retry policy instead of restating them.

An engine is a type of its own rather than a configured instance because that is what lets it be registered with `AddSearchProvider<T>()` and stay idempotent: a factory registration could not be deduplicated and a second call would ask the same engine twice per query.


## ServiceCollectionExtensions.AddBrowserSearch

`2026-09-13`

*Registers search with the browser-backed providers included.*

Pulls in both halves deliberately: browser search is HTTP search plus a page reader, and a consumer that asked for it wants the HTTP providers alongside rather than instead. Both registrations are idempotent, so calling them again elsewhere costs nothing.

Google is not here. It answers a form rather than an address — measured on 2026-09-13, its results address returns `/sorry/` where its own form returns results — and walking a form needs the page agent, which this package deliberately does not depend on. It ships in `Snail.Toolkit.Web.Search.Browser.Agent`, and a deployment that wants both adds that one too.


## Brave.AnsweredAsync

`2026-09-14` · *internal*

The document alone, because this engine counts requests rather than pages. Measured on 2026-09-14 from one address, arms alternating and a fresh context each: the full page cost 71 to 73 requests and was answered `429` three times out of three, the document alone cost one request and was answered `200` three times out of three — with all eighteen result blocks in it, because the results are rendered on the server. The engine was not refusing us; we were spending its budget inside one page.


## BrowserSearch.ResolvedAsync

`2026-09-14` · *internal*

*Turns the addresses a results page carries into the addresses they lead to.*

An engine whose markup states the address it means needs nothing here, and the default does nothing. Google is the reason this exists: measured on 2026-09-14, every result on its page is a relative `/goto` link whose query is an encrypted blob, and the address behind it is known only by asking the engine where the link leads. Asking costs one request per link, which is why the engine that pays it also caps the list here rather than handing over everything its parser found.

This runs before `IsOutbound` and not after, because a wrapper carries no host at all: the filter would read every one of them as belonging to the engine and drop the whole page. Until 2026-09-14 it did exactly that, and Google answered a megabyte of markup with no results.


## Bing

`2026-09-15`

*Asks Bing through a real browser.*

The same engine this library already asks over plain HTTP, read the way that works. Measured on 2026-09-15 from a datacentre address, five queries each: over HTTP the engine answered «"Роснано"» with bible sites, «Роснано» with a hotel chain, «Rosnano» with Zurich tourism and «отзыв лицензии КИВИ Банк» with car parts — ten confident results per query, about something else entirely. Through a browser the same address answered «Роснано» with Группа РОСНАНО, the Wikipedia article and the company's own press centre, three times out of three, and «отзыв лицензии КИВИ Банк» with «Отозвана лицензия на осуществление банковских операций у…» and «Что известно об отзыве лицензии у Киви-банка».

THE FIRST READ IN A FRESH CONTEXT IS THE UNRELIABLE ONE. Of three queries measured three times each, two failed on their first attempt — one with an empty page, one with a navigation that never settled — and answered properly on the second and third. That is why the page is taken whole rather than as the document alone: `DocumentOnly` was measured too, and it returned the stale or wrong page where the full load returned the right one.

Every result link is wrapped in the engine's own redirect — `/ck/a?…&u=a1<base64url>` — and unwrapping it costs nothing, unlike Google's `/goto`, which has to be asked where it leads. The address is inside the wrapper, base64url after the two-character marker.


## Bing.AnsweredAsync

`2026-09-15` · *internal*

The whole page, not the document alone. Measured on 2026-09-15 against the same three queries: the document alone answered «"QIWI Банк"» with the same four links the plain HTTP client gets, while the full load answered with the article about the licence being revoked — the engine finishes the page with script, and the half we would save is the half with the results in it.


## BrowserSearch.Parsed

`2026-09-15`, `2026-09-13` · *internal*

*Reads the links off one results page, already parsed.*

The document rather than its markup since 2026-09-15, and the reason is the ceiling on nesting depth: an engine handed a string parses it itself, and four engines parsing their own were four ways past the one door the parser is supposed to have. Parsing here also means an engine cannot forget it.

Markup that cannot be followed must answer with nothing rather than throw: the caller then records an answered attempt that found nothing, and an engine whose layout changed shows up in the metrics as one that suddenly always answers empty. An exception per query would only look like an outage.

The results are handed over whole and the ceiling is applied by the caller of this, so an engine supplies a parser and nothing else. Until 2026-09-13 no ceiling was applied here at all: `MaxResultsPerProvider` was read by the two engines that fetch over HTTP and by neither of the two that drive a browser, and Brave alone answered with a hundred links.


## BrowserSearch.Answers

`2026-09-15` · *internal*

*Whether the page the engine answered with is an answer to the query that was asked.*

The default says yes, because most engines either answer or refuse and there is no third thing to name. Bing is the third thing, measured on 2026-09-15: asked in Russian from a datacentre address it sometimes serves a page of ten confident results about something else entirely — bible sites for «Роснано», Italian pages about e-cigarettes for «отзыв лицензии КИВИ Банк» — and a parser cannot tell that from an answer, because structurally it IS one.

An engine that can be served that page overrides this and says so, and the search then records a refusal rather than results. Left here rather than pushed into every engine because the caller's question — «did I get an answer to what I asked» — is the same for all of them; only the evidence differs, and only an engine that has been measured should claim to have any.


## ServiceCollectionExtensions.AddBing

`2026-09-15`

*Adds Bing, read through a browser instead of over plain HTTP.*

TAKES THE PLACE OF THE HTTP PROVIDER OF THE SAME NAME, and that is the point rather than a side effect. Measured on 2026-09-15 from a datacentre address: over HTTP the engine answered four Russian queries out of five with ten confident results about something else — bible sites for «Роснано», a hotel chain, Zurich tourism, car parts — while the same address read through a browser answered with the company, its Wikipedia article and the news about its licence.

Two providers under one name would be worse than either: a report that says «Bing answered nothing» could no longer say which Bing, and the aggregator would spend two of its slots per query on one engine. So the browser reading replaces the plain one wherever this method is called, and a deployment with no browser keeps the plain one by simply not calling it.


## BrowserSearch.FindAsync

`2026-09-20`

Bounded by `Timeout`, the same setting that bounds an engine asked over HTTP. Until 2026-09-20 it bounded only those: it was wired into the HTTP client's pipeline and nowhere else, so a browser engine lasted as long as its navigation timeouts times its retries — a consumer measured 136 to 392 seconds against a one-minute setting. Running out of it is a fault, as it is over HTTP, and a cancellation by the caller stays a cancellation.
