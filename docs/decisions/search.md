# Snail.Toolkit.Web.Search

*Generated from the compiler's documentation by `dotnet run --project gen -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

7 decisions recorded in this package, each naming the day it was measured. Declarations a consumer cannot see are marked *internal*: they are here because the reasoning is, not because the type is reachable.

## Bing

`2026-09-12`

*Reads Bing's results page.*

An HTTP provider rather than a browser one because it answers a plain request: measured on 2026-09-12 from a datacentre address it returned results where Google answered a captcha and Ecosia refused outright. That is the whole reason to keep more than one engine registered — which of them answers a given client from a given address is a fact that changes, and the aggregator is where it stops mattering.

Every result link is wrapped in a redirect whose target sits base64url-encoded in its `u` parameter behind an `a1` marker. Left wrapped, every address would be bing.com and deduplication across providers — the reason the address is a `Link` at all — would collapse the whole answer to one entry.

One thing is measured and unresolved: asked for a bare tax number rather than a brand name, this engine's page mentions the number but named no outside site that the unwrapper could recover, while Brave, the DuckDuckGo endpoint and Yandex all answered with the registries. Whether that is the engine or the reading is not yet known, so an identifier query should not be trusted to this provider alone.


## SearchOptions.RefusalCooldown

`2026-09-12`

*Gets or sets how long a provider is left alone after its first refusal.*

A challenge page is not an error in the query but a reaction to how often it was asked: measured on 2026-09-12, one engine answered, challenged, answered, challenged and answered again over one evening, with nothing changing between the requests but their spacing. Each further refusal doubles the rest until it reaches `MaxRefusalCooldown`; any answer, with results or without, ends it.


## ServiceCollectionExtensions.AddSearchParts

`2026-09-12` · *internal*

*Registers the providers that answer a plain request, the search that merges them, and the memory it consults first.*

Two engines rather than one, because which of them answers a given client from a given address is a fact that changes: measured on 2026-09-12 from a datacentre address, one returned results while several others answered with a challenge. Each provider is one request per query, so a deployment that wants only one registers it on its own instead of taking the pair.

The memory of refusals is one per process on purpose — a rest recorded by one search has to hold for the next — and the clock it reads is registered beside it, so a host that keeps its own `TimeProvider` wins and a test can move time instead of waiting it out.


## WebSearch

`2026-09-12`

*Runs a batch of queries across the registered providers, a few per query and in turn.*

Deduplication is why several providers are worth asking at once, and it is decided by `Link` equality rather than by string comparison: grouping on the raw address kept the same page three times. One provider failing is a recorded attempt, never an exception — a slow or blocked back end must not take the whole answer with it. A provider that refused is recorded as refused rather than faulted, and is left out of the following searches until its rest is over: the engines measured on 2026-09-12 closed over frequency, not fingerprint, and asking a challenged engine again at once is how a pause becomes a ban. For the same reason a query goes to `ProvidersPerQuery` providers rather than to all of them, the next query to the next ones in the round, so that a batch spreads what it asks.


## PolitenessHandler

`2026-09-14`

*Holds the per-host gate for the length of one request, and hands the gate what the site answers back.*

This replaces a hand-rolled throttling handler that kept its own semaphore and its own per-host clock, so the browser side and the HTTP side paced themselves independently and a host could be hit by both at once. The gate is the core's, and every transport now waits at the same one. The turn is held until the response headers arrive, not until the body is read: a slow download must not keep the next request to that host waiting, and the gap that matters to a site is between requests.

A site asking to be left alone is written into that same gate. Until 2026-09-14 only the browser side did this — `Rest` had exactly one caller — so a `429` answered to a search engine slowed nothing, and a browser visit to the same host walked straight into a site that had just said it had had enough.


## ServiceCollectionExtensions.AddSearchClient

`2026-09-14`

*Adds the named HTTP client a provider fetches through.*

The order of the handlers is the point. Resilience is outermost, so every retry re-enters the politeness gate and is made under a freshly drawn identity; the other way round, a retry storm would hold one host's gate for the whole backoff and repeat the same user agent that was just refused.

The deadline is the resilience pipeline's, not the client's: the standard handler sets `Timeout` to infinite so that its own total-request timeout owns the clock. Setting the client timeout beside it is therefore a setting that silently does nothing, which is why `Timeout` is applied here instead — together with the per-attempt timeout and the sampling window, because the pipeline refuses a total shorter than the attempt it contains.

The exit is the configured one, which it had not been until 2026-09-14. The browser has always honoured `Web:Identity:Proxies` because its context is built from the identity, while this client set no primary handler at all and went out directly. The consequence worth naming is not the leak but the correlation: a query arrives from the proxy and the request that resolves its links arrives from the real address seconds later, and the party seeing both is the one that handed out the link.

The builder is handed back so a consumer can add a header of their own, or replace the exit where one client has to leave by another route.


## ServiceCollectionExtensions.IsWorthRepeating

`2026-09-14` · *internal*

*Answers whether asking again could do better than repeating the refusal.*

The standard handler retries a `429` and waits the `Retry-After` the site sent, which is right until the number is larger than the caller has. Measured on 2026-09-14 against a handler that counts what reaches it: a gap of three seconds cost four requests and nine seconds, inside a thirty-second budget; a gap of thirty, inside a budget of twenty, made one request and then spent the whole budget waiting to be refused a second time, ending in a timeout that named neither the status nor the gap. The site had already said both in the first answer, and this hands that answer back instead of sitting on it.

The gate still holds the host for the full gap — `PolitenessHandler` writes it there on the way out — so declining to wait here makes nobody ruder, only better informed.
