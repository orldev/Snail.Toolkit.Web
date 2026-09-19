# Snail.Toolkit.Web.Search

*Generated from the compiler's documentation by `dotnet run --project gen -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

The public surface of this package: 16 types. Everything else it contains is internal and is described under [Decisions](../decisions/index.md) where it carries a measurement.

## Bing

Reads Bing's results page.

An HTTP provider rather than a browser one because it answers a plain request: measured on 2026-09-12 from a datacentre address it returned results where Google answered a captcha and Ecosia refused outright. That is the whole reason to keep more than one engine registered — which of them answers a given client from a given address is a fact that changes, and the aggregator is where it stops mattering.

Every result link is wrapped in a redirect whose target sits base64url-encoded in its `u` parameter behind an `a1` marker. Left wrapped, every address would be bing.com and deduplication across providers — the reason the address is a `Link` at all — would collapse the whole answer to one entry.

One thing is measured and unresolved: asked for a bare tax number rather than a brand name, this engine's page mentions the number but named no outside site that the unwrapper could recover, while Brave, the DuckDuckGo endpoint and Yandex all answered with the registries. Whether that is the engine or the reading is not yet known, so an identifier query should not be trusted to this provider alone.

**ProviderName** — The name this provider is registered and its HTTP client is keyed by.


## DuckDuckGo

Reads the DuckDuckGo HTML endpoint.

The HTML endpoint is used rather than the JavaScript one because it is the same answer without a browser, which is the whole reason this package can exist without Playwright. Robots rules are not consulted here, and that is deliberate: this fetches the provider's own search endpoint, published for exactly this, not the pages it names. Whoever reads those pages afterwards is the one that has to obey them.

**FindAsync** — *no summary*

Asks for a further page only while one is wanted and the page itself offers one, so the usual cost stays a single request. A caller that raised `MaxResultsPerProvider` past what one page holds used to be handed the page with nothing saying why the number was smaller than asked for.

The offset of the next page is read out of the page's own next-page control rather than counted from a page size written down here. A size constant would be a guess about someone else's markup, and a wrong one either skips results or asks for the same page until the ceiling stops it.

**ProviderName** — The name this provider is registered and configured under.


## ISearchCooldown

Remembers which providers refused recently and keeps them out of the next searches for a while.

Politeness towards the engine, as distinct from politeness towards a host: the per-host gate spaces the requests that are made, and this decides whether one is made at all. A provider that has just answered with a challenge will answer the next request the same way if it comes at once, and every such request makes the eventual ban more likely — so the search stops asking rather than asking more carefully.

**Answered** — Records that a provider answered, which ends its rest and forgets its earlier refusals.

An empty answer counts: the engine let the request through, and that is the whole question here.

**Refused** — Records that a provider refused, and answers how long it will now be left alone.

A refusal that arrives while the rest still runs is not a further refusal: it is a request that was already in flight when the engine closed, and it answers what is left of the running rest.

**RestingUntil** — When the rest of a provider that must not be asked ends, or nothing when it may be asked.

The instant rather than a yes-or-no since 2026-09-15, and for a reason outside this interface: a provider left out of a search is a fact the CALLER has to be able to state — "not asked, resting until 14:20" reads differently from "asked and said nothing", and a boolean can only produce the first half of that sentence. Answering nothing rather than an instant in the past keeps the question single: a caller asks whether it may ask, and gets the reason it may not.


## ISearchProvider

A search back end that can be asked one query.

Says nothing about transport. The contract used to carry an HTTP client name, which forced every provider through `IHttpClientFactory` and left a provider driving a browser holding a client it never opens, wrapped in proxy, rotation and throttling handlers that never run. A provider that needs a browser lives in the companion package and implements this same interface.

**FindAsync** — Runs one query and answers with links, or with the reason there are none.

Failure is data here as it is everywhere else in this toolkit. A provider used to be contracted to throw, and three exception types existed to carry what an outcome carries: the search then had to tell a refusal from a fault by catching the right base class, and the two engines that fetch over HTTP never threw the refusal at all — they called `EnsureSuccessStatusCode`, so a 429 arrived as a fault and the engine asking to be left alone was never left alone.

An empty list is success: the provider answered and the answer held nothing, which is a fact about the query rather than about the engine. Cancellation is still an exception, because that is what the TPL and ASP.NET Core expect of it.

The return type is written out here rather than behind the `SearchAnswer` alias this package uses internally, because an alias is a fact about one compilation and does not travel in a NuGet package. A consumer writing their own provider is welcome to the same short name by declaring it themselves:

```csharp
global using SearchAnswer = Snail.Toolkit.Web.Outcome<
    System.Collections.Generic.IReadOnlyList<Snail.Toolkit.Web.Search.SearchResult>,
    Snail.Toolkit.Web.Search.SearchFailure>;

```

It is not shipped in the package on purpose. A global using arriving with a dependency lands in every file of the consumer's project whether they wanted it or not, and collides outright with the one they may already have declared — which is exactly what the first consumer to implement this interface did.

**Host** — The host this provider's queries go to, or nothing when it does not talk to one.

Nothing by default because not every provider is a site: an index in the same process answers with no request at all, and giving it a host would put it behind a gate built for strangers.

A provider that names its host can be asked what it costs before it is asked for results. The price is not a number this interface invents — it is the per-host gate, which already holds the gap this deployment decided for that host and the moment the host next opens, and until 2026-09-15 the search could not reach it because a provider was an opaque name. The engines differ by three orders of magnitude here: measured on 2026-09-13, one of them answers at one request a second and another only at one every two minutes, and a caller with a deadline cannot spend the second on the first.

**Name** — Identifies the provider in configuration, metrics and logs.

Must equal the name the provider is registered under. Declare it as a `const` on the implementation and return that constant here, so registration and runtime cannot drift.


## IWebSearch

Asks every registered provider and merges what they answer.

**FindAsync** — Runs every query against every provider and collects what came back.

**FindAsync** — Runs every query the budget allows, and says which providers it did not stretch to.

An overload rather than a defaulted parameter, so a search stays one short line for the callers who are in no hurry — which is nearly all of them, and the shape every caller had before 2026-09-15.

WHAT A CALLER IS SAYING HERE IS WHAT THE QUERY IS WORTH, not which engine to use. The engines differ by three orders of magnitude — measured on 2026-09-13, one answers at a request a second and another only at one every two minutes — so a caller with a deadline had no way to ask the cheap ones only. NAMING THE ENGINE WAS THE OBVIOUS ANSWER AND IS NOT THIS ONE: the aggregator exists because which engine answers a given client from a given address is a fact that changes, and a caller that names engines has taken that fact back. A budget says the same thing in the caller's own terms and leaves the choice where the measurements are. A caller who genuinely wants one engine registers one, or resolves `ISearchProvider` and asks it directly; neither costs anything.

The budget is spent on waiting, not on cutting: a provider whose host does not open inside it is not asked at all and says so as `OutOfTime`, which is knowable before the request because the per-host gate holds it. What is already in flight when the budget runs out is cancelled, and comes back under the same kind rather than as `Canceled` — the caller did not change its mind, the search kept the caller's promise.

**StreamAsync** — Yields each provider-and-query attempt as it finishes, failures included.

Results are not deduplicated here — deduplication needs the whole answer, and a caller streaming attempts has chosen to see them arrive instead.

**StreamAsync** — Yields each attempt as it finishes, and each one the budget did not stretch to.


## IdentityHandler

Presents a coherent identity on every outgoing request.

The headers come from the same rotation the browser side uses, so a user agent claiming Chrome on Windows is never sent beside client hints claiming macOS. Only headers a real client sends on every request are set; `Accept` and `Accept-Encoding` are left to `HttpClient`, which varies them the way a browser does. The identity's proxy is deliberately not applied. A pooled handler cannot switch proxy per request, and pretending otherwise would route traffic somewhere other than the configuration says — a consumer that needs proxying configures it on the primary handler of the named client.


## PolitenessHandler

Holds the per-host gate for the length of one request, and hands the gate what the site answers back.

This replaces a hand-rolled throttling handler that kept its own semaphore and its own per-host clock, so the browser side and the HTTP side paced themselves independently and a host could be hit by both at once. The gate is the core's, and every transport now waits at the same one. The turn is held until the response headers arrive, not until the body is read: a slow download must not keep the next request to that host waiting, and the gap that matters to a site is between requests.

A site asking to be left alone is written into that same gate. Until 2026-09-14 only the browser side did this — `Rest` had exactly one caller — so a `429` answered to a search engine slowed nothing, and a browser visit to the same host walked straight into a site that had just said it had had enough.


## SearchAttempt

What one provider did with one query.

Attempts travel back beside the results because an empty result set is ambiguous on its own: it reads the same whether nothing matched or every provider was down.

**Duration** — Gets how long the attempt took.

**Failure** — Gets why the attempt carried nothing, or `null` when it carried something.

**IsAnswered** — Gets whether the provider answered, whether or not the answer held anything.

**Provider** — Gets the provider that was asked.

**Query** — Gets the query it was asked.

**Results** — Gets the links the provider offered.


## SearchCooldown

Keeps a refused provider out of the set for a rest that doubles with every further refusal.

The first refusal starts a rest of `RefusalCooldown`; a refusal after the rest has run out doubles it, up to `MaxRefusalCooldown`; an answer clears the record. The run of refusals is remembered across rests on purpose: an engine that refuses, waits out five minutes and refuses again is saying that five minutes was not enough. Only a refusal counts — a fault leaves the record untouched, because resting an engine over a parser that no longer follows its markup would hide the one thing that needed fixing.

A refusal inside a running rest leaves it as it is. Until 2026-09-20 each one doubled it, and a search running four requests at once, or a batch that kept asking, turned one refusal into the one-hour ceiling: measured that day, five queries to one refusing engine rested it for an hour instead of five minutes.

There is no separate probing state. A rest that has run out lets the next request through, and the answer to that request either clears the record or doubles the rest; that is the whole mechanism.


## SearchFailure

The reason an attempt produced no results.

Kept apart from `SearchFailureKind` alone because a fault is only actionable with the message the provider threw, while `NoResults` has nothing to add.

**OpensAt** — Gets the soonest this provider could be asked, when that is known.

Carried as an instant rather than only in the message, for the same reason `PageFailure.RetryAfter` is: a caller deciding whether to wait or to report has to compare it with a clock. One field rather than one per kind, and the wording is the gate's own (`IPoliteness.OpensAt`), because the two kinds that set it are the same fact with different causes: `Resting` is the engine holding itself back after a refusal, `OutOfTime` the per-host gate holding it back — and a caller writing "not asked, ask again after X" does not care which.

**OutOfTime** — The reason a provider nobody reached before the budget ran out was left out of a search.

No instant here on purpose: the budget ran out, and when this provider's host would next open is a question nobody asked — answering it would mean consulting the gate for an engine the search has already decided against, and an invented instant reads exactly like a measured one.

**Resting** — The reason a provider was left out of a search rather than asked.

**Shut** — The reason a provider whose host stays shut for longer than one request may take was left out.

The same fact as `TooLate` measured against a different promise: there the caller named a budget, here the deployment named how long one request may take. Both are the gate answering before the request rather than the request waiting at it.

**TooLate** — The reason a provider whose host opens too late was left out of a search.

**Unreadable** — The reason a page was refused by the ceiling on nesting depth rather than read.

Worded once because four engines say it: the message names the engine, since a caller running several of them has to know whose page it was, and names the ceiling rather than the depth the page reached — the parser stops counting at the ceiling and never learns how much deeper the markup went.

**Unrelated** — The reason a page of results about something else is recorded as a refusal.

A refusal rather than an empty answer, because that is what it is: the engine did not answer THIS caller, and the caller that hears "nothing found" would go on to say so about the world. It rests the engine, which is the right reaction to a wall served as a page of links.


## SearchFailureKind

Why one attempt carried no results.

**Canceled** — The caller cancelled before the provider answered.

**Faulted** — The provider threw.

**NoResults** — The provider answered and the answer held nothing.

This is an answer, not a failure of the provider — which is why `IsAnswered` counts it as one.

**OutOfTime** — The provider was not asked: it could not answer inside the budget this search was given.

Apart from `Canceled`, which it used to arrive as, and the difference is who decided: a caller that cancels has changed its mind, while this is the search keeping the promise the caller made it. Folded together, a deployment could not tell a deadline it had set from a user closing a page.

Two shapes of the same fact. Either the host this engine talks to does not open inside the budget — known BEFORE asking, from the per-host gate, and then `OpensAt` names when it would have — or the budget ran out while other providers were answering.

**Refused** — The provider was turned away: a challenge, a consent wall, a rate limit.

Kept apart from `Faulted` because the two call for opposite reactions. A fault is a defect on this side — a parser that no longer follows the markup, a socket that died — and the right answer is to look at it. A refusal is the engine asking to be left alone, and the right answer is to leave it alone for a while, which is what `ISearchCooldown` does with it. Folding the two together, as the search once did, would have rested an engine over our own bug.

**Resting** — The provider was not asked at all: it is resting after a refusal.

Not a failure of the engine and not an answer from it — it is this library declining to ask, and until 2026-09-15 it was visible only in the log. The caller needs it because the two sentences it tells apart call for opposite reactions: "every engine answered and nothing matched" is a fact about the query, while "the engines that know this kind of question are all resting" is a fact about the search, and both arrived as an outcome with no results in it.

The instant the rest ends travels with it in `OpensAt`, because a caller writing that sentence for a person has to name a time rather than reproduce prose.

**Unreadable** — The engine answered, and the answer is markup this library will not parse.

Kept apart from `Faulted`, which it would otherwise be folded into, because the two send a reader to different places. A fault is a defect on this side and the answer is to look at it; this is a page refused on purpose by a ceiling on nesting depth, and there is nothing to fix — the measurement behind that ceiling is recorded on the parser. Kept apart from `NoResults` for the sharper reason: that one is a statement about the QUERY, and reporting a page nobody read as "nothing matched" is the single answer here that would be untrue.

It rests no engine. A refusal is the engine asking to be left alone; markup too deep to read says nothing about how often it is willing to be asked.


## SearchOptions

How many providers are asked at once, how much each may answer, and how long a refused one is left alone.

**AlwaysAsked** — Gets or sets the providers asked on every query, outside the round.

The round exists to spare engines that close over frequency, and a provider that costs nothing to ask — a local index — has nothing to be spared: left to the round, its answer would be a matter of luck. Named rather than typed, because whether a provider is cheap is a fact about the deployment.

**MaxDegreeOfParallelism** — Gets or sets how many provider-and-query pairs may be in flight at once.

**MaxRefusalCooldown** — Gets or sets the longest rest a run of refusals can grow to.

**MaxResultsPerProvider** — Gets or sets how many results one provider may contribute to one query.

A ceiling per provider rather than per answer, so one talkative provider cannot crowd out the rest after deduplication.

**ProvidersPerQuery** — Gets or sets how many providers one query is sent to.

The round, not the set: with four engines registered and two here, one query goes to two of them and the next query to the other two, so a batch spreads its load instead of landing on everybody at once. Completeness survives it — results are merged and deduplicated across the batch — and the engines live longer, which is what the measured ones were lost to. A number at or above the count of registered providers asks all of them, as the search always did.

**RefusalCooldown** — Gets or sets how long a provider is left alone after its first refusal.

A challenge page is not an error in the query but a reaction to how often it was asked: measured on 2026-09-12, one engine answered, challenged, answered, challenged and answered again over one evening, with nothing changing between the requests but their spacing. Each further refusal doubles the rest until it reaches `MaxRefusalCooldown`; any answer, with results or without, ends it.

**SectionName** — The configuration section this binds from.

**Timeout** — Gets or sets how long one provider request may take, retries included.

Spent by the resilience pipeline rather than by `Timeout`, and divided into a per-attempt budget and a circuit-breaker sampling window. The circuit breaker refuses a window shorter than 500 ms, which is what puts a floor of one second under this setting; anything less is refused when the host starts rather than surfacing as a pipeline validation error on the first query.

A browser engine is held to the same number since 2026-09-20, and on both paths it includes the wait for the host's turn. An engine whose host keeps a gap longer than this — Google's is measured in minutes — runs out of it when asked again before the gap is over, which is the setting doing what it says.


## SearchOutcome

The links a search found, and what every provider did to find them.

An empty answer explains itself here rather than leaving the caller to guess: `IsAnswered` separates "every provider answered and nothing matched" from "no provider answered at all", which read identically as an empty list and call for opposite reactions.

**Attempts** — One entry per provider and query, failures included.

**HasResults** — Gets whether anything was found.

**IsAnswered** — Gets whether at least one provider answered, with or without results.

**Results** — The links, deduplicated across providers.


## SearchResult

One link a provider offered, with the words it offered them under.

The address is a `Link` rather than a string, which is what makes deduplication across providers work at all: the same page arrives from two of them as http and https, with and without a trailing slash, and carrying whichever campaign parameters each attached. There is deliberately no page content here. The moment this record carries what a page says, this package and the scraping one are the same package under two names.

**Address** — Gets the address the link points at.

**Provider** — Gets the provider that offered the link.

**Snippet** — Gets the provider's own summary of the page — never the page itself.

**Title** — Gets the title the provider showed for the link.


## ServiceCollectionExtensions

Registers the providers, their HTTP clients, the memory of refusals and the search that merges them.

**AddSearch** — Registers search, binding every option class from configuration.

**AddSearch** — Registers search configured in code.

**AddSearchClient** — Adds the named HTTP client a provider fetches through.

The order of the handlers is the point. Resilience is outermost, so every retry re-enters the politeness gate and is made under a freshly drawn identity; the other way round, a retry storm would hold one host's gate for the whole backoff and repeat the same user agent that was just refused.

The deadline is the resilience pipeline's, not the client's: the standard handler sets `Timeout` to infinite so that its own total-request timeout owns the clock. Setting the client timeout beside it is therefore a setting that silently does nothing, which is why `Timeout` is applied here instead — together with the per-attempt timeout and the sampling window, because the pipeline refuses a total shorter than the attempt it contains.

The exit is the configured one, which it had not been until 2026-09-14. The browser has always honoured `Web:Identity:Proxies` because its context is built from the identity, while this client set no primary handler at all and went out directly. The consequence worth naming is not the leak but the correlation: a query arrives from the proxy and the request that resolves its links arrives from the real address seconds later, and the party seeing both is the one that handed out the link.

The builder is handed back so a consumer can add a header of their own, or replace the exit where one client has to leave by another route.

**AddSearchProvider** — Adds one more provider to the set every query is run against.

Registered as an enumerable so providers accumulate instead of replacing each other, and idempotent per implementation type so a second call cannot ask the same back end twice per query.


## WebSearch

Runs a batch of queries across the registered providers, a few per query and in turn.

Deduplication is why several providers are worth asking at once, and it is decided by `Link` equality rather than by string comparison: grouping on the raw address kept the same page three times. One provider failing is a recorded attempt, never an exception — a slow or blocked back end must not take the whole answer with it. A provider that refused is recorded as refused rather than faulted, and is left out of the following searches until its rest is over: the engines measured on 2026-09-12 closed over frequency, not fingerprint, and asking a challenged engine again at once is how a pause becomes a ban. For the same reason a query goes to `ProvidersPerQuery` providers rather than to all of them, the next query to the next ones in the round, so that a batch spreads what it asks.

**StreamAsync** — *no summary*

The validation stays in a method that is not an iterator, and the walk lives in the one below it: an iterator defers its whole body to the first enumeration, so a caller handed a null list of queries would learn about it later and somewhere else.
