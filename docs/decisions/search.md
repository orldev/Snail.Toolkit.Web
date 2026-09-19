# Snail.Toolkit.Web.Search

*Generated from the compiler's documentation by `dotnet run --project gen -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

20 decisions recorded in this package, each naming the day it was measured. Declarations a consumer cannot see are marked *internal*: they are here because the reasoning is, not because the type is reachable.

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

`2026-09-14`, `2026-09-20` · *internal*

*Answers whether asking again could do better than repeating the refusal.*

The standard handler retries a `429` and waits the `Retry-After` the site sent, which is right until the number is larger than the caller has. Measured on 2026-09-14 against a handler that counts what reaches it: a gap of three seconds cost four requests and nine seconds, inside a thirty-second budget; a gap of thirty, inside a budget of twenty, made one request and then spent the whole budget waiting to be refused a second time, ending in a timeout that named neither the status nor the gap. The site had already said both in the first answer, and this hands that answer back instead of sitting on it.

The gate still holds the host for the full gap — `PolitenessHandler` writes it there on the way out — so declining to wait here makes nobody ruder, only better informed.

A refusal that names no gap is not repeated either, which it was until 2026-09-20: a `429` or a `503` without the header cost four requests before the search heard about the first one, and the search rests an engine the moment it hears. Asking a challenged engine again at once is how a pause becomes a ban — the rule this toolkit states everywhere and practised against itself here. A server that merely failed is repeated as before: a `500` is not the site turning anybody away.


## DuckDuckGo.PageAsync

`2026-09-15` · *internal*

An instance method since 2026-09-15, because the wall is read here: the detector is a dependency, and a page of this engine can be a challenge whatever status carried it.


## ISearchCooldown.RestingUntil

`2026-09-15`

*When the rest of a provider that must not be asked ends, or nothing when it may be asked.*

The instant rather than a yes-or-no since 2026-09-15, and for a reason outside this interface: a provider left out of a search is a fact the CALLER has to be able to state — "not asked, resting until 14:20" reads differently from "asked and said nothing", and a boolean can only produce the first half of that sentence. Answering nothing rather than an instant in the past keeps the question single: a caller asks whether it may ask, and gets the reason it may not.


## ISearchProvider.Host

`2026-09-15`, `2026-09-13`

*The host this provider's queries go to, or nothing when it does not talk to one.*

Nothing by default because not every provider is a site: an index in the same process answers with no request at all, and giving it a host would put it behind a gate built for strangers.

A provider that names its host can be asked what it costs before it is asked for results. The price is not a number this interface invents — it is the per-host gate, which already holds the gap this deployment decided for that host and the moment the host next opens, and until 2026-09-15 the search could not reach it because a provider was an opaque name. The engines differ by three orders of magnitude here: measured on 2026-09-13, one of them answers at one request a second and another only at one every two minutes, and a caller with a deadline cannot spend the second on the first.


## IWebSearch.FindAsync

`2026-09-15`, `2026-09-13`

*Runs every query the budget allows, and says which providers it did not stretch to.*

An overload rather than a defaulted parameter, so a search stays one short line for the callers who are in no hurry — which is nearly all of them, and the shape every caller had before 2026-09-15.

WHAT A CALLER IS SAYING HERE IS WHAT THE QUERY IS WORTH, not which engine to use. The engines differ by three orders of magnitude — measured on 2026-09-13, one answers at a request a second and another only at one every two minutes — so a caller with a deadline had no way to ask the cheap ones only. NAMING THE ENGINE WAS THE OBVIOUS ANSWER AND IS NOT THIS ONE: the aggregator exists because which engine answers a given client from a given address is a fact that changes, and a caller that names engines has taken that fact back. A budget says the same thing in the caller's own terms and leaves the choice where the measurements are. A caller who genuinely wants one engine registers one, or resolves `ISearchProvider` and asks it directly; neither costs anything.

The budget is spent on waiting, not on cutting: a provider whose host does not open inside it is not asked at all and says so as `OutOfTime`, which is knowable before the request because the per-host gate holds it. What is already in flight when the budget runs out is cancelled, and comes back under the same kind rather than as `Canceled` — the caller did not change its mind, the search kept the caller's promise.


## Refusals.Wall

`2026-09-15` · *internal*

*Reads a challenge page as the refusal it is, whatever status carried it.*

The other half of this class, and the half that was missing until 2026-09-15. A status says nothing when the wall arrives as a successful one: measured that day, an engine answered `202` with a captcha page, the parser found no results in it, and the search recorded NO RESULTS — a statement about the QUERY — for a page that was a statement about the ADDRESS. The engine was then asked again at the next query, because only a refusal rests an engine, which is how a wall becomes a ban.

The signal is named in the message because the caller cannot see the page: "refused" alone sends a reader to look at the query, and `markup:confirm this search was made by a human` sends them to look at the address.


## Relevance

`2026-09-15`, `2026-09-20` · *internal*

*Tells an answer to the query from a page of confident results about something else.*

The worst of the three shapes an engine can answer in. An empty page is read as "nothing found" and a refusal is read as a refusal, but a page of ten unrelated links is structurally an answer: measured on 2026-09-15 from a datacentre address, one engine answered «"Роснано"» with biblical sites and Netflix, «Роснано» without the quotes with a chain of hotels, and «отзыв лицензии КИВИ Банк» with car parts and Microsoft sign-in pages. A parser cannot tell that from an answer; a reader can, at a glance, and this is that glance written down.

THE RULE AND THE NUMBERS BEHIND IT. What is counted is the share of results whose title or snippet holds at least one word of the query of four letters or more. The same measurement put a real answer at eight to ten out of ten and a stub at zero out of ten — two clusters with nothing in between, and the ceiling here is a third, which is in that gap and closer to the stub. Until 2026-09-20 the engine that had this check asked for ONE result out of however many, so a stub carrying a single accidental match passed as an answer.

WHAT A WRONG ANSWER COSTS, both ways. Reading a stub as an answer hands the caller ten confident links about nothing, and the caller has no way to tell. Reading an answer as a stub records a refusal, which rests that engine for the configured cooldown — the other engines answer the query meanwhile and the rest ends by itself. The second is the cheaper mistake, which is why the ceiling sits low rather than in the middle.

Words shorter than four letters are left out because they match everything: an engine's own chrome, a navigation link, the word "the". Matching is by substring and case-insensitive, so a title inflecting a word the query gave in another form is a miss rather than a match — the rule is deliberately crude, since what it separates is a cluster at eight from a cluster at zero.


## SearchFailureKind.Resting

`2026-09-15`

*The provider was not asked at all: it is resting after a refusal.*

Not a failure of the engine and not an answer from it — it is this library declining to ask, and until 2026-09-15 it was visible only in the log. The caller needs it because the two sentences it tells apart call for opposite reactions: "every engine answered and nothing matched" is a fact about the query, while "the engines that know this kind of question are all resting" is a fact about the search, and both arrived as an outcome with no results in it.

The instant the rest ends travels with it in `OpensAt`, because a caller writing that sentence for a person has to name a time rather than reproduce prose.


## WebSearch.Awake

`2026-09-15` · *internal*

*The providers that may be asked right now, with the resting ones named in the log.*

A search in which every provider rests asks nobody and comes back unanswered — which is the truth, and louder in the log than the usual line, because from the caller's side it looks like a search that found nothing. Since 2026-09-15 the caller is told the same thing in the answer rather than only here: the log is for whoever runs the process, and this is a fact about the search the process was asked to run.


## WebSearch.AttemptAsync

`2026-09-15` · *internal*

*Asks one provider one query, and turns every way that can end into an attempt.*

A cancellation has to say WHOSE it was: the budget running out is `OutOfTime`, and everything else stays `Canceled`. Told apart nowhere else, a deadline a deployment set would read exactly like a user closing a page. The condition is on the BUDGET's token, not on the one the work runs under: that one also fires when the caller stops reading, and a provider that timed out inside itself — its own client's clock, nothing to do with this search — must not be reported as the budget's doing either. A search without a budget has nothing that can run out.

A SECOND HALF WAS WRITTEN HERE AND TAKEN OUT, and the reason is recorded because it is the obvious thing to propose again: `&& !caller.IsCancellationRequested`, to keep a caller's own cancellation out of this arm. MEASURED BY REMOVAL on 2026-09-15 — no test could tell the two apart, and none can: when the caller cancels, the fan-out this attempt is written to is reading under that same token, so the enumeration ends and the attempt is never yielded to anybody. A condition nothing can observe is a claim about behaviour that does not exist.


## WebSearch.Turn

`2026-09-16` · *internal*

*The providers one query goes to: the next few in the round, or every one when the round holds them all.*

The round runs over the providers awake for this search, so a resting one is skipped rather than counted as a turn, and its share goes to the ones that can take it. A provider named in `AlwaysAsked` stands outside the round and joins every query.

A PLACE IN THE ROUND GOES TO AN ENGINE THAT CAN USE IT. Measured by a consumer on 2026-09-16: four engines, two places per query — both places went to an engine resting after a refusal and one whose parser was faulting, and the engine that answers that subject well was never asked. So an engine that has refused, or that has already faulted in this batch, stands aside while a healthy one is free.

It stands aside, it is not dropped: a fault is still asked for once, because resting an engine over our own defect is how the defect stops being visible, and when there are not enough healthy engines to fill the places the faulted ones take them again. Nothing here is remembered past this batch — a parser is mended by a release, not by a timer, and the next search finds out for itself.


## SearchCooldown

`2026-09-20`

*Keeps a refused provider out of the set for a rest that doubles with every further refusal.*

The first refusal starts a rest of `RefusalCooldown`; a refusal after the rest has run out doubles it, up to `MaxRefusalCooldown`; an answer clears the record. The run of refusals is remembered across rests on purpose: an engine that refuses, waits out five minutes and refuses again is saying that five minutes was not enough. Only a refusal counts — a fault leaves the record untouched, because resting an engine over a parser that no longer follows its markup would hide the one thing that needed fixing.

A refusal inside a running rest leaves it as it is. Until 2026-09-20 each one doubled it, and a search running four requests at once, or a batch that kept asking, turned one refusal into the one-hour ceiling: measured that day, five queries to one refusing engine rested it for an hour instead of five minutes.

There is no separate probing state. A rest that has run out lets the next request through, and the answer to that request either clears the record or doubles the rest; that is the whole mechanism.


## SearchOptions.Timeout

`2026-09-20`

*Gets or sets how long one provider request may take, retries included.*

Spent by the resilience pipeline rather than by `Timeout`, and divided into a per-attempt budget and a circuit-breaker sampling window. The circuit breaker refuses a window shorter than 500 ms, which is what puts a floor of one second under this setting; anything less is refused when the host starts rather than surfacing as a pipeline validation error on the first query.

A browser engine is held to the same number since 2026-09-20, and on both paths it includes the wait for the host's turn. An engine whose host keeps a gap longer than this — Google's is measured in minutes — runs out of it when asked again before the gap is over, which is the setting doing what it says.


## WebSearch.AffordableAsync

`2026-09-20` · *internal*

*Asks a provider, unless it rests or the budget cannot reach it — both known before the request.*

The check is made as each pair starts rather than when the work is planned, because the gate moves while a search runs: an engine that was open when the batch began is closed a moment later by the query ahead of it, and a decision made at planning time would be wrong for every pair but the first.

The same holds for a rest, and until 2026-09-20 this reasoning was applied to the gate and not to it: the resting set was read once per batch, so an engine that refused the first query was asked every query after it. Measured that day — five queries, five requests to an engine that had already said no.

A search without a budget is bounded all the same, by `Timeout` — the longest one request may take. An engine whose host opens after that could only be asked in order to be timed out, and hearing when it opens is worth more than spending the time to be told nothing.

A provider that names no host is always asked. It is not a promise that it is quick — it is that this library knows nothing about its cost, and refusing to ask on a guess would be worse than paying it.
