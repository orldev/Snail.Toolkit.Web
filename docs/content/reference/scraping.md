# Snail.Toolkit.Web.Scraping

*Generated from the compiler's documentation by `dotnet run --project docs/generator -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

The public surface of this package: 46 types. Everything else it contains is internal and is described under [Decisions](../decisions/index.md) where it carries a measurement.

## Appetite

How much of a page the browser is allowed to fetch.

A site that counts requests rather than pages reads one ordinary page load as a burst, because that is what it is. Measured against Brave on 2026-09-14 from one address, arms alternating and a fresh context each: the full page cost 71 to 73 requests and was answered `429` three times out of three, while the document alone cost one request and was answered `200` three times out of three — carrying all eighteen result blocks, because that engine renders its results on the server.

So this is not a way around anything. Fetching the images, fonts, styles and beacons of a page that will only be parsed is bandwidth nobody wanted and load the site did not have to carry; asking for the document is both lighter on the site and what gets an answer. It stays a choice rather than a default because a page that is driven rather than read needs its scripts: Google's query is typed into a form that does not exist without them.

**DocumentOnly** — The document alone; every subresource it asks for is refused.

**Everything** — Everything the page asks for, the way a person's browser would.


## Attempts

Draws an identity, asks the site, waits for its turn, does the work, and repeats it where that is worth doing.

The pipeline is built once, in the constructor, because this is a singleton per result type: an earlier version of the same idiom was a field initializer on a scoped service, so a new Polly pipeline was assembled for every scope. Retries are driven by `IsTransient` rather than by an empty answer, so a consent wall is retried with a new identity while an empty document is accepted as the site's answer.

Measuring is here rather than in each caller because an attempt is what there is to measure, and because two of the three callers had no instruments at all: an errand and a call to a site's own service each load a page and neither was counted as one.


## BrowserFailure

Reads what a browser threw as the kind of failure a caller can act on.

Matching on the message is not a shortcut: it is the only route Playwright leaves open. Version 1.62 exposes exactly one public exception type, `PlaywrightException` — its `TargetClosedException` is declared `internal` and cannot be named by a consumer, and no timeout type exists at all. A locator wait is the one place that raises `TimeoutException` from `System`; navigation never does, so catching that alone once left every navigation timeout falling through to a catch-all.

Shared rather than private to the reader, because the kind is what decides whether another attempt is worth making. The reader classified and the two that drive pages did not: everything a browser threw at them became `Unexpected`, which is not transient, so a browser that had lost its connection was indistinguishable from a selector that names nothing and no retry could tell them apart.

**IsBrowsers** — Answers whether this is an exception a browser is expected to raise.

The filter every caller needs, written once. Anything else is a defect in this library rather than an answer from the site, and is left to escape.

**Of** — Reads one exception from a browser as a failure.


## BrowserPool

Keeps one browser alive and hands out permission to have a page open on it.

A lease used to be a whole browser process, held for the length of a visit, so the number of pages in flight could never exceed the number of processes the pool was willing to run — one, at the defaults. A lease is now one page's worth of permission on a shared browser, and the ceiling is how many pages may be open at once rather than how many processes may exist.

MEASURED on 2026-09-14 by `probes/contexts`, which is the whole reason this is shaped this way. On stock Playwright a browser process with one page costs 284 MB and every further context on it costs 70 — four times cheaper — and opening one takes 133 ms against 340 for a process, with nothing degrading up to sixteen. So processes buy nothing a context does not buy cheaper.

The same probe answered something that removed the choice altogether: against our own browser image the count of browser processes in the container stays at ONE however many connections are made, because that image publishes an already-launched browser. A pool that counted connections was calling them browsers and getting one either way. Keeping several was therefore not a trade between isolation and memory — it was a setting with no effect, and it is gone.

What is given up is stated plainly: a browser that crashes takes every page in flight with it, where several processes would have cost a fraction. Against a browser server that cannot be bought back at any price, and the retry in `IAttempts` is what answers it.

WHAT WAS TAKEN OUT OF HERE ON 2026-09-15, and what deliberately was not. This file was 439 lines and had been the source of a defect three times over. Three of its four jobs came out cleanly and are now `Driver` (start Playwright, open a browser of the configured engine), `Permits` (how many pages at once, and the idle clock) and `Reaper` (close a browser nobody is using). Two of the three historical defects lived in what is now the first and second of those.

THE FOURTH — opening the browser — WAS PROPOSED AND REFUSED, and the reason is written here because it is the obvious next thing to suggest about a class that is still the longest in the package. It reads as a separable job and is not one: what makes it correct is the two things it shares with shutdown, and both are things a second object could only be handed a copy of. The gate it waits on is linked to `_shutdown`, which also releases callers queued in `QueuedAsync` for a page — one cancellation serving two gates, and the remark on `OpeningAsync` says outright that the two are written as a pair and for the same reason. And what decides whether a browser that has finished opening is published or closed is `_isDisposed`, read at the one instant that races shutdown; handed across an object boundary it becomes two facts that have to be kept in step by whoever remembers to, which is the exact shape of the defect that cost a hung test host.

There is a third, smaller reason and it is the one this library cares about most: all three refusals a caller can meet in there name `BrowserPool`. Moved, they would name a type the caller has never heard of — which is word for word the failure the remark on `OpeningAsync` exists to prevent.

**DisposeAsync** — *no summary*

Every step here is something that can fail, and none of them may take the rest down with it. The shutdown token is cancelled first because it is what releases a caller already queued for a page; the reaper is awaited for its ending rather than its answer; and the driver and the tokens are released in a `finally`, because a browser that refuses to close would otherwise leak the process that hosts it.

NEITHER GATE IS DISPOSED — not `_opening` here, and not the one inside `Permits`, which is why that type has no `Dispose` to call. A caller released after shutdown would meet a disposed object, and nothing here holds a handle worth reclaiming: `SemaphoreSlim` allocates one only for `AvailableWaitHandle`, which nobody asks for. `Dispose` does not wake anybody waiting on it either — a caller queued behind an opening browser was left on a gate nobody would open again and never returned at all. MEASURED on 2026-09-15: the test host had to be killed.

The rule held for the page semaphore from the start and was not extended to this one, which is exactly the kind of omission a paragraph cannot prevent and a type can. Half of it is now enforced by `Permits` having no way to be disposed; this field is the half still kept by hand, and it is kept here rather than moved because what it guards — publishing the browser — is what shutdown races with.

**RentAsync** — *no summary*

A caller that arrives after disposal and one that was already queued when it began are told the same thing. The guard alone answered only the first of them: the second waited on a gate nobody would ever open again, which is how a shutdown with work in flight used to hang instead of ending.


## BrowserTraits

What a browser reports that an HTTP client has nothing to say about.

These traits used to sit on the shared identity, where they were dead weight for a transport with no screen and no pointer — and worse than dead weight, because a value nobody can honour is a claim waiting to contradict the rest of the fingerprint. They are derived from the identity rather than chosen beside it, so the scale factor and the touch flag still agree with the user agent.

**DeviceScaleFactor** — Gets the device pixel ratio.

**For** — The traits this identity reports, the same ones every time it is presented.

The viewport is chosen by the user agent rather than drawn per visit, which is the correction made on 2026-09-14. A random draw gave one identity a different screen on every visit — the same browser, on the same machine, resized between two page loads — and that is a contradiction of exactly the kind this type was extracted to end. People do resize windows, but the pairing of a screen with a machine is the stable part, and an identity is a machine.

One call answers both the context and the injected script, so a page's window size cannot disagree with what its scripts see. That held before by reusing one draw and holds now by construction.

**HasTouch** — Gets whether the identity describes a touch device.

**ViewportHeight** — Gets the viewport height in CSS pixels.

**ViewportWidth** — Gets the viewport width in CSS pixels.


## ChromiumArguments

The Chromium command line a browser is launched with.

Split into switches that hide automation and switches that weaken the browser. The second group is no longer unconditional: scraping renders documents an attacker controls, so dropping the sandbox is a decision the caller has to make deliberately through `Sandboxed`.

**For** — Builds the command line for the given settings.


## ClientHints

Rewrites the browser's user-agent metadata over CDP so a page reads the identity it was told about.

Measured on 2026-09-11 against a live site: the context sent `Sec-Ch-Ua: "Google Chrome";v="140"` while the page still read `HeadlessChrome;151` from `navigator.userAgentData`, and the site's analytics carried the headless build away in its `uafvl` parameter. Playwright has no context option for this — `BrowserNewContextOptions.UserAgent` spoofs the user-agent string alone — so the metadata has to be set through `Emulation.setUserAgentOverride`, which only Chromium answers. A header and a script that disagree are a stronger signal than either would be on its own, which is why failing to align is reported rather than passed over.

Chromium only, and the check is on the engine rather than on the exception it would raise: `NewCDPSessionAsync` throws on Firefox and WebKit, and a warning logged on every page of a Firefox run would say nothing anyone can act on.


## CrawlOptions

The ceilings a whole-domain read runs under, and what it hands back.

Reading one page and reading a domain are separate operations because they cost different amounts and are worth different things: an answer to a specific question is buried by completeness, and a crawl is partly successful by nature. These are the defaults every crawl starts from; a caller narrows them per crawl.

The knob that decides how fast a crawl runs is not here. A crawl is one host, and the gate that paces requests is per host, so `Web:Politeness:MaxConcurrentPerHost` governs it — raising `Web:Scraping:MaxDegreeOfParallelism` only adds workers that queue at the same gate.

**Budget** — Gets or sets how long a single crawl may run.

A crawl that hits this keeps everything it read and says the time ran out, because a partial answer to "what is on this site" is worth having and an exception is not.

Forty minutes rather than ten, decided on 2026-09-13. A page costs eight to nine seconds on the defaults — a second of politeness, one request per host, behaviour simulated — so ten minutes bought about seventy pages against a ceiling of five hundred: the ceiling was decoration and every crawl of a real site ended as `TimeLimit` whatever `MaxPages` said. This is still short of what the ceiling allows, and deliberately so: the two are a budget and a limit, not one number written twice, and a crawl is an operation whose price the caller is meant to see.

**IncludesSubdomains** — Gets or sets whether subdomains of the starting host count as the same site.

Off, because "everything about this site" normally means one host, and one careless flag turns a read of a documentation site into a read of the whole company. On, it is the shape a security audit wants.

**IsNewestFirst** — Gets or sets whether the pages a sitemap lists are read newest first, by the dates the site gives them.

A newspaper's sitemap index runs for years. Measured on 2026-09-12: read in index order, a ceiling of five hundred pages filled with whatever the site listed first — two hundred and sixty-four sponsored articles from 2021 — while the news of the week never came up. Newest first opens the most recently dated parts of an index before the rest and takes the most recently dated pages of what they list. A site that dates nothing is read in its own order either way.

**IsSitemapPreferred** — Gets or sets whether a published sitemap is used instead of following links.

**MaxDepth** — Gets or sets how many links deep the crawl follows when it has to follow links at all.

Ignored on a site with a sitemap: there is no depth in a list.

**MaxPages** — Gets or sets how many pages a single crawl may read.

**MaxSitemapBytes** — Gets or sets the largest sitemap document that is read.

**MaxSitemaps** — Gets or sets how many sitemap documents one crawl may open.

A large site publishes an index of hundreds of parts, and reading all of them is its own crawl.

**MaxTextCharacters** — Gets or sets how many characters of each page's text the crawl keeps.

Deliberately far below what one page may have. This ceiling multiplies by the number of pages, so a budget that is generous for a single fetch fills an agent's whole context on the twentieth page. Full markup is never kept at all — a crawl answers what a site says, and one address is read again with `ReadAsync` when the document itself is wanted.

**SectionName** — The configuration section this binds from.

**SitemapTimeout** — Gets or sets how long one sitemap document may take to download.


## CrawlPage

One page a crawl read.

This is deliberately not `PageContent`: it carries no markup. Five hundred pages of rendered HTML is hundreds of megabytes held at once to answer a question about what a site says, and the caller that wants one document reads that one address again.

**Address** — Where the page was finally read from, after any redirect.

**Depth** — How many links from the start it was found — zero for the start and for a sitemap.

**FetchedAt** — When it was read.

**IsTruncated** — Gets whether the text was cut to fit a budget, the crawl's or the reader's.

**OriginalLength** — Gets how long the text was before any budget was applied.

**Text** — What the page says, under the crawl's own text budget.

**Title** — Gets the page's own name, or `null` when it has none.

The one thing kept from the head: an index built over a crawl needs to name what it found, and the first line of the text is not the name of the page.


## CrawlReport

What one crawl found, and how it ended.

A crawl is partly successful by nature — some pages answer, some are blocked, some time out — so the outcome is a report rather than a list of pages or a single failure. `Stop` is the part a caller cannot work out for itself: pages alone cannot say whether the site was read or merely sampled.

**Elapsed** — Gets how long the crawl took.

**Failed** — Gets how many addresses answered with a failure.

**IsFromSitemap** — Gets whether the addresses came from the site's own sitemap rather than from its links.

Worth reporting because it changes what the answer covers: a sitemap lists pages no navigation links to, while following links can only reach what is linked.

**Pages** — Gets every address the crawl attempted, in the order they finished.

**Read** — Gets how many pages were read.

**Start** — Gets the address the crawl started from.

**Stop** — Gets why the crawl ended.


## CrawlScope

What one crawl is allowed to do.

Every ceiling is stated rather than defaulted here, so that the configured defaults live in exactly one place. Ask `ScopeOf` for a scope and narrow it: `scope with { MaxPages = 50 }`.

**Budget** — Gets how long the crawl may run.

**Covers** — Answers whether an address is one this crawl reads: on the site, and not in an excluded section.

Asked of an address from the site's own list and of one found in a link alike, so a section left out is left out however the crawl arrived at it. The predicates live on the scope rather than in the crawl because they are what the scope means, and because the crawl is not the only thing that asks.

**Excluded** — Gets the path prefixes that are not read at all — a sponsored section, a catalogue of codes.

Compared against the path as the site wrote it, from its first character and without regard to case, whether the address came from the sitemap or from a link. Not defaulted from the configuration, because which section of a site is noise is a fact about that site alone; the start itself is never excluded.

**IncludesSubdomains** — Gets whether subdomains of the starting host count as the same site.

**IsNewestFirst** — Gets whether the pages a sitemap lists are read newest first, by the dates the site gives them.

**IsOnTheSite** — Answers whether an address belongs to the site being read.

A leading `www.` is dropped before the subdomain comparison, so a crawl started at `www.example.com` with subdomains on still covers `docs.example.com`. No public suffix list is consulted: the site is whatever the starting address named, never a guess at the company behind it.

**IsSitemapPreferred** — Gets whether a published sitemap is used instead of following links.

**MaxDepth** — Gets how many links deep the crawl follows where it has to follow links.

**MaxPages** — Gets how many pages may be read.

**Start** — Gets the address the crawl starts from, which also decides the site.


## CrawlStop

Why a crawl ended.

A crawl that stopped early and one that read the whole site look identical from the pages alone, and the difference decides whether the answer can be trusted as complete. That is why a report states this rather than leaving a caller to compare counts against ceilings.

**DepthLimit** — The depth ceiling was reached while following links; deeper pages were not read.

**Exhausted** — Everything in scope was read.

**PageLimit** — The page ceiling was reached; the site has more.

**Refused** — The starting address was not one that could be read at all.

**TimeLimit** — The time budget ran out; the site has more.


## Crawler

Reads a list of addresses, or discovers and reads a whole site.

The fan-out itself lives in the core, where search reuses it. What is decided here is how many pages may be open at once, how far the walk goes, and when to stop.

Where a site's addresses come from is `ISiteIndex`'s, not this class's. Following links is the fallback, not the plan: a site that publishes a list hands over its own pages — including the ones nothing links to — and reading that list is the batch this class already does. The link walk exists for sites that publish none, and it runs level by level so that the frontier, not the call stack, holds what is left to read.


## Document

A page the browser has finished with, before anyone has decided what it is.

Written as a record rather than as six parameters because three of them are strings and two of those are a document and an address. A call that swapped them would compile, run, and answer with a page whose text was its own URL.

**Address** — Gets the address the read was asked for.

**FinalUrl** — Gets the address the browser ended on, which redirects make different from the one asked for.

**Html** — Gets the markup as the browser has it after scripts have run.

**Identity** — Gets the identity the page was shown to the site under.

**Rendered** — Gets the text the page renders, which is what emptiness is judged on.

**StatusCode** — Gets the status the navigation answered with, when there was a response.


## Engine

Which browser engine pages are read with.

A deliberate choice rather than a detail, because not everything the toolkit presents survives a change of engine. Client hints belong to Chromium alone: Firefox never sends `Sec-Ch-Ua`, so a browser claiming Chrome without them contradicts itself, and the alignment itself goes over CDP, which no other engine answers. The launch switches are Chromium's own as well.

It sits at the root of the package rather than beside the pool, and that is not tidiness. Living in `Browsers` it was read by `Stealth`, which `Browsers` reads in turn, and the two namespaces depended on each other in a cycle — invisible to the compiler, which only ever sees one assembly, and found by asking the solution rather than by reading it. The engine is not a property of the pool anyway: the setting that names it is here, and everything that has to agree with it reads it from here.

**Chromium** — Chromium, the only engine that answers CDP.

**Firefox** — Firefox, driven over the juggler protocol.

**WebKit** — WebKit, driven over its own protocol.


## FailureKind

The kinds of failure a page read can report.

A kind is what a caller branches on: `Blocked` asks for a different identity or proxy, `Timeout` asks for a longer budget, `RobotsDisallowed` asks for nothing at all. The engine this came from answered every one of these with an empty string, so a caller could see that a page was missing but never why.

THERE IS DELIBERATELY NO KIND FOR CANCELLATION, and the reason is that every way a read can be cut short already has a name of its own. A caller cancelling propagates as `OperationCanceledException`, which is what the TPL and ASP.NET Core expect and what `CrawlerTests` holds this library to. A crawl outliving its own budget is `TimeLimit` in the report, told apart from the caller's cancellation by the one guard that can tell them apart — whose token was cancelled. A navigation outliving `NavigationTimeoutMs` is `Timeout`. A search outliving `SearchOptions.Timeout` is `SearchFailureKind.Canceled`. A kind added here would be one nothing could produce, which is worse than the gap it was meant to fill.

**Blocked** — The site answered with a challenge, a captcha or a refusal page.

**BrowserUnavailable** — No browser could be launched or reached.

**Empty** — The page loaded and rendered nothing at all.

Distinct from `Blocked` on purpose: a genuinely empty document is a fact about the site, while a challenge page is a fact about the client, and the two call for opposite reactions.

**Navigation** — Navigation failed outright — DNS, TLS, a refused connection or an HTTP error status.

**Proxy** — The configured proxy could not be used.

**RobotsDisallowed** — The site's robots rules forbid this URL for the configured agent.

**Timeout** — The page did not load within the configured navigation budget.

Cancellation requested by the caller is not reported here: it propagates as `OperationCanceledException`, which is what the TPL and ASP.NET Core expect.

**Unexpected** — Something failed that this library does not classify further.

**Unreadable** — The page loaded, but its markup was refused rather than parsed.

Today there is one way to earn this and it is nesting depth: parsing is quadratic in it, so markup a thousand tags deep costs seconds of CPU per page and any site can serve it in a hundred kilobytes.

A member was added here where `Cancelled` was refused, and the difference is the whole test for whether a kind should exist: ANY SITE CAN PRODUCE THIS ONE, with five thousand opening tags and no closing ones, while nothing in the library could ever have produced a cancellation that was not already named elsewhere. The alternative was worse than a new member rather than merely different — refused markup would have come back as `Empty`, which says the site published nothing when in fact it published a great deal and we declined to read it.


## HttpRobotsSource

Downloads `robots.txt` over HTTP, through the proxy the rest of the visit uses.

This is the one place a transport is needed to obey a site's rules, and it is deliberately not a browser page — see `ProxiedHttp` for why, and for why the clients are held per proxy. A site that cannot be asked is treated as permitting the fetch. RFC 9309 suggests the opposite for a server error, but failing closed on a transient 502 would stop a crawl silently and look exactly like a bug; the warning in the log is the deliberate trade.


## HttpSitemapSource

Downloads a sitemap over HTTP, decompressing the gzipped ones.

Compression is decided by the first two bytes rather than by the file name or the content type. A sitemap is routinely served as `.xml.gz` with no `Content-Encoding`, which no HTTP client decompresses on its own, and just as routinely served gzipped from a name that says `.xml`.

The read is capped. Compressed XML expands by twenty to one and the file is written by the site being read, so an unbounded read is a memory limit set by someone else.


## HumanBehavior

Moves the mouse along a curve, scrolls in uneven steps and hovers something interesting.

Bounded by `Budget`. Without it a page that never settles turned an optional flourish into an unbounded wait, and the scroll script in particular was awaited with neither a timeout nor a cancellation token.


## HumanBehaviorOptions

How much human-like activity to perform on a loaded page.

Every setting here used to be unreachable from configuration: the registration passed a hard-coded instance that also silently overrode the defaults declared on this class, and the pause range was a tuple, which the configuration binder cannot fill. The range is two properties for that reason.

**Budget** — Gets or sets the budget for the whole simulation on one page.

Simulation is decoration, not the goal. Without a ceiling a page that never settles turns an optional flourish into an unbounded wait, which is what scraping can least afford.

**Enabled** — Gets or sets whether to simulate activity at all.

Simulation costs seconds per page. It earns them against a site that scores behaviour and wastes them against one that does not, so it is a decision the caller makes per deployment.

**HoverProbability** — Gets or sets the chance of hovering an interactive element, from 0 to 1.

**MaxPauseMs** — Gets or sets the longest pause between actions, in milliseconds.

**MaxScrollDistance** — Gets or sets how far the page may be scrolled, in CSS pixels.

**MinPauseMs** — Gets or sets the shortest pause between actions, in milliseconds.

**MouseSteps** — Gets or sets how many points a mouse path is drawn through.

**SectionName** — The configuration section this binds from.


## IAttempts

Runs work against a site under one whole attempt, and makes another when the first was worth repeating.

An attempt is the unit everything here is arranged around: a freshly drawn identity, the site's ruling on that identity, its turn at the per-host gate, and then the work. Everything a retry could change sits inside it — the identity is drawn per attempt, because that is what makes a second one worth making, and the robots ruling and the gate follow that identity rather than one left over from an attempt that failed.

This exists because the sequence was written out by hand three times, and the three drifted. The page reader carried a retry pipeline from the start; the site call was given one only after a measurement showed one run in five lost to a navigation timeout; the agent had none at all, so a browser that went away in the middle of a form lost the errand outright. One of them is now the sequence and the other two ask for it.

**AtAsync** — Makes attempts at `address` until one succeeds or the failure is final.


## IBrowserLease

Permission to have one page open on the pooled browser, until it is disposed.

It used to be exclusive use of a whole browser process, which is why the toolkit could never read more pages at once than it ran browsers — one, at the defaults. A context costs a quarter of a process and opens in a third of the time (measured by `probes/contexts` on 2026-09-14), so what is worth rationing is pages rather than processes.

Disposing gives the permission back. Failing to dispose removes it from circulation permanently, so a lease belongs in an `await using` and nowhere else.

**Browser** — Gets the leased browser.


## IBrowserPool

A pool of running browsers, launched on demand and closed when idle.

**RentAsync** — Takes a live browser from the pool, waiting for a free one when all are in use.

The returned browser is checked for a live connection first, so a crashed process or a restarted container is replaced rather than handed out. An earlier implementation cached one browser and tested it for `null` only, so every request after a disconnect failed until the consuming process was restarted.


## IClientHints

Makes the metadata a page can read agree with the client hints the context sends.

**AlignAsync** — Aligns `navigator.userAgentData` with the identity the context was opened under.


## ICrawler

Reads many pages: a list of addresses already known, or a whole site that is not.

The two belong together because the second is built on the first. A site that publishes a sitemap is not crawled at all — its own list of pages is read, which is faster, more complete and far less traffic than following links, and only a site that publishes none is walked link by link.

**CrawlAsync** — Reads a whole site and reports what it found and why it stopped.

Minutes and tens of pages, against seconds for a single address. Cancellation propagates; running out of the scope's own time budget does not, because everything read so far is the answer.

**ReadAllAsync** — Reads every address and answers once they are all done.

**ScopeOf** — Builds the scope a crawl gets when the caller states nothing but where to start.

Narrow it with a `with` expression rather than restating the configured ceilings.

**StreamAsync** — Streams each outcome the moment it is ready, in completion order.

A failed address is a result, not an exception: one unreachable site must not cancel the batch. Every outcome carries its own address — the page on success, the failure on everything else — so nothing has to be matched back to the input by position.


## IHumanBehavior

Performs the activity a real visitor would produce on a loaded page.

**SimulateAsync** — Moves, scrolls and pauses on the page, giving up quietly when the budget runs out.

Never fails the read: simulation is decoration on top of a page that has already loaded, so a closed target or an exhausted budget ends the simulation rather than the request.


## IKeepsakes

Keeps what a site left with us, so the next visit to it is not a first visit.

A cleared challenge is a cookie: Cloudflare answers with `cf_clearance`, a consent wall with a preference, a registry with a session. All of them are the site saying «this one may pass», and all of them die with the browser context. A reader that opens a fresh context per page therefore arrives as a stranger every time and is challenged every time — slower for us and noisier for the site.

A persistent profile would be the obvious answer and is not available: `launchServer` takes no user data directory, so a browser reached over `ws://` cannot be given one. What is available is the storage state — cookies and local storage, read out of one context and put into the next — and it carries exactly the part that matters.

The shipped implementation keeps it in memory for `RememberSiteFor` and forgets a host nobody returns to. A deployment that wants it to survive a restart writes its own and registers that instead: the state is a string, and where it is written is a decision about the deployment's own storage, not about this library. Whoever does that should know what they are keeping — it is the site's permission, and it identifies the visitor.

**Keep** — Keeps what this host said, replacing whatever it said before.

The newest answer wins outright rather than being merged: the state is the browser's own summary of the context it came from, and half of one summary beside half of another is not a state any browser held.

**Of** — What this host left with us, or nothing when it left nothing.


## IPageReader

Fetches one page with a real browser and reports what came back.

**ReadAsync** — Reads `address`, retrying the failures worth retrying.

Answers with an outcome rather than a string so a caller can tell a blocked request from a timeout from a genuinely empty page. Only cancellation propagates as an exception — an address that is not an absolute http one comes back as a failure, because a list of links to read routinely holds one.

**ReadAsync** — Reads `address`, fetching only as much of it as the appetite allows.

An overload rather than a defaulted parameter, so that reading a page stays one short line for the callers who want the whole of it — which is nearly all of them.


## IPageSnapshots

Takes a picture of a page with a real browser.

The text of a page says what it says; a picture says what it shows — a chart, a map, a layout, a page whose content is drawn rather than written. Added on 2026-10-04 for the MCP server built on this toolkit, where the reader is a model that can look.

A picture is a visit like any other: the same identity, the same politeness gate, the same robots rules and the same judgement of a wall, because it goes through the same `IAttempts` and `IVisits` the page reader does. A site that would refuse the text refuses the picture.

**TakeAsync** — Pictures the screen a visitor sees on arriving at `address`.

**TakeAsync** — Pictures as much of `address` as the frame takes in.


## ISiteIndex

The list of its own pages a site publishes, where it publishes one.

Reading a whole domain is usually not a crawl at all: a site that publishes an index hands over the list of its pages, including the ones nothing links to, and reading a list of addresses is a batch the reader already does. Following links is the fallback for sites that publish none.

A port of its own because the crawl has two questions and only one of them is walking: where the addresses come from is the other, and it is the one that changes. Conditional requests, a remembered index, a list a caller supplies from somewhere else — each is another answer to this and none of them is a change to the walk.

**ListedAsync** — Asks the site for its own list, taking at most `room` addresses from it.


## IStealthScript

Produces the script injected before any page script runs.

**For** — Builds the script that makes the browser agree with `identity`.


## IVisit

A prepared page and the identity it presents, held until disposal returns the browser to the pool.

**Identity** — The identity the page presents, needed to explain a failure after the fact.

**Page** — The page to navigate or drive.


## IVisits

Opens browser pages already dressed as the identity they claim.

The preparation a page needs before it is safe to show to a site — the context built from the identity, the stealth script, the client-hint metadata, the navigation timeouts — is the same whether the page is going to be read once or driven through a form. Keeping it in one place is not tidiness: the defect that prompted this seam was a context that announced one browser in its headers while the page reported another in script, because the two were set in two places and drifted apart.

**OpenAsync** — Rents a browser and hands back a page prepared under the given identity.

The appetite is stated rather than defaulted, so that a caller driving a page through its own scripts says so where it can be read beside the code that depends on it.


## PageContent

A page that was fetched and rendered.

Markup and text are both carried because the two callers want opposite things and neither can recover the other for free: an agent wants the text and drowns in the markup, while a search engine reads its links out of the markup and finds none in the text. Only the text is budgeted. A ceiling applied to markup lands in the middle of a tag, which is no longer a document any parser can follow, and everything past the cut — links included — disappears without a word.

`Final` is kept apart from `Requested` because a redirect to a consent wall or a `/sorry/` page is the most common way a read silently stops being about the page that was asked for. Both are `Link` rather than strings, so `IsRedirected` compares canonical forms: an http address answered over https is the same page, and comparing the raw strings called that a redirect.

**FetchedAt** — When the document was read.

**Final** — Gets the address the browser ended on after redirects.

**Html** — The document as the browser rendered it, whole.

**IsRedirected** — Gets whether the browser was redirected away from the requested address.

**IsTruncated** — Gets whether the text was cut to fit the configured budget.

Carried beside the text as well as announced inside it, so a caller can act on the fact — widen the budget, warn the reader — without parsing prose.

**OriginalLength** — Gets how long the text was before any budget was applied.

**Requested** — The address the caller asked for.

**StatusCode** — Gets the HTTP status of the main document, when the browser reported one.

**Text** — What a reader would see on that document, after any budget was applied.

**Title** — Gets the document's own name, or `null` when it has none.

**UserAgent** — Gets the user agent the page was fetched with.


## PageContents

Turns a finished document into the content it carries, or the reason it carries none.

This tail was written twice — once at the end of a plain read and once at the end of a walkthrough — and the two copies drifted, the way two copies do. What drifted was the title: a page read by address carried one and the same page walked to through a form did not, so one property of one type had two answers depending on the road the page arrived by. Extracting the tail is not tidiness; it is the only thing that stops that class of defect from happening again.

What is deliberately NOT here is the refusal and the HTTP status. A challenge on arrival and a challenge after a form was submitted are different facts worded differently, and the status a walkthrough carries belongs to the page it started on rather than to the one it ended on. Folding those in would have made one of the two callers say something untrue.

**Judged** — The content the document carries, or the reason it carries none.

Emptiness is judged on the rendered text rather than on the markup: a browser answers `<html><head></head><body></body></html>` for a blank page, so testing the markup for whitespace never finds one.

The budget lands on the extracted text and the markup goes back whole. Cutting markup by character ends the document mid-tag, and the callers that read markup — every browser-backed search engine — then lose every link past the cut while the page still looks like it parsed.

SO NOTHING HERE BOUNDS THE MARKUP, and that is why the one bound it does have is a refusal rather than a cut. Parsing is quadratic in nesting depth, so markup nested a thousand tags deep costs seconds of CPU per page — and this is the only place that can say so in the caller's own vocabulary. Left to itself the refusal would have arrived as an empty text and been reported as `Empty`: a page that published a great deal, described as having published nothing.


## PageFailure

Why a page read failed, in the amount of detail a caller can act on.

The original exception is kept on `Cause` instead of being flattened into `Message`. Reading a page fails for reasons that look alike in a log line and differ completely in what they demand — a Cloudflare interstitial and a DNS failure both end a request, and only one of them is worth retrying with a new proxy.

**Address** — Gets the address the failure happened on.

**Cause** — Gets the exception this failure was translated from.

**IsRefusal** — Gets whether the site turned the request away, as distinct from failing to answer it.

A wall, a `429`, a `403` and a `503`: the four shapes in which a site says it will not answer this caller now. The HTTP side of the toolkit has read the same three statuses as a refusal since 2026-09-14; the browser side read only a wall and a `429` until 2026-09-20, so a browser engine answering `403` was a fault — never rested, asked again at every query. What a refusal calls for is asking less, which is the opposite of what a fault calls for, and that is why the two are told apart.

**IsTransient** — Gets whether trying again with a different identity or proxy could succeed.

Retrying a `RobotsDisallowed` or an `Empty` answer only repeats the same work: the site already gave its final answer. The same is true of most client errors — a 404 is a fact about the URL, and retrying it three times with exponential backoff spends minutes to be told the same thing. A 429 is the exception, because it explicitly asks for a later attempt, as does anything the server blames on itself.

**Kind** — The classification to branch on.

**Message** — A human-readable description of the failure.

**RetryAfter** — Gets the gap the site asked to be left alone for, when it asked for one.

A `429` is the site saying how often it is willing to be asked, and `Retry-After` is the only part of that answer with a number in it. Until 2026-09-14 nothing here read the header at all, so a refusal by frequency was answered with three more requests on a schedule of our own — the toolkit's reply to "too often" was to ask again, sooner than asked.

**StatusCode** — Gets the HTTP status the site answered with, when navigation produced a response.

**UserAgent** — Gets the identity that was in use, so a repeated block can be traced to a fingerprint.


## PageReader

Loads a page in a pooled browser under a freshly generated identity.

What surrounds the fetch — the identity, the site's ruling on it, its turn at the per-host gate, the retry when the answer was worth asking again for — belongs to `IAttempts` and is not restated here. This class is what happens once all of that has been arranged: open a page, read it, and say what it turned out to be.


## PageSnapshot

A picture of a page as the browser drew it.

Deliberately not a part of `PageContent`. A reader that wants the text of five hundred pages must not be handed five hundred pictures with it, and a caller that wants to see one page has no use for its markup; the two are different questions with answers of very different weight.

**Final** — Gets the address the page was finally drawn at, after any redirect.

**Height** — Gets the height of what was pictured, in the page's own pixels.

**Image** — The picture, encoded as `MediaType`.

**IsCut** — Gets whether the page ran on below the picture.

True for a screen with more page under it, and for a whole page taller than the ceiling. A picture that stops without saying so reads as a page that ended.

**MediaType** — The encoding of every picture this library takes.

JPEG rather than PNG because the picture is taken to be looked at, usually by a model, and a page of text and photographs is three to ten times smaller this way with nothing a reader would miss.

**Requested** — The address that was asked for.

**StatusCode** — Gets the HTTP status the site answered with.

**TakenAt** — When it was taken.

**Title** — Gets the page's own name, or `null` when it has none.

**Width** — Gets the width of what was pictured, in the page's own pixels.


## PageSnapshots

Pictures a page through the same visit the page reader makes.

**Quality** — The JPEG quality every picture is encoded at.

**TallestPage** — How tall a picture of a whole page may be, in the page's own pixels.

A page is as long as its author likes, and an endless feed is longer than that. A picture of all of it would be tens of megabytes nobody can look at — a model scales a picture down to something near two thousand pixels a side — so the ceiling is a few screens, and `IsCut` says when it was reached.


## ScrapingOptions

How browsers are launched and how hard a page is tried.

Bound through the standard options pattern and validated when the host starts, so a setting that cannot work refuses to bring the process up instead of surfacing on the first URL. Identity and proxies are not here: they belong to every transport, so they live in the core's own section.

**BrowserArguments** — Gets or sets extra Chromium command line switches.

Ignored when `BrowserServer` is set: the server owns its own command line.

**BrowserIdleTimeout** — Gets or sets how long an unused browser is kept before it is closed.

**BrowserServer** — Gets or sets the WebSocket endpoint of a Playwright browser server to connect to.

Empty launches a browser in this process. A value must be the `ws://` endpoint a `playwright run-server` prints — not a proxy address, which is what an earlier name invited callers to put here.

**Engine** — Gets or sets which browser engine pages are read with.

Chromium by default, because everything the toolkit aligns is aligned for it: client hints are set over CDP, which no other engine answers, and the launch switches are Chromium's own. Firefox earns its place where the browser must not announce itself — `navigator.webdriver` is compiled into Playwright's Firefox and no preference turns it off — and such a build is reached through `BrowserServer`, since a launched Firefox is Playwright's own and carries the announcement.

**Headless** — Gets or sets whether launched browsers run without a window.

**IgnoreCertificateErrors** — Gets or sets whether TLS certificate errors are ignored.

Was unconditionally on before, which hides a misconfigured MITM proxy and accepts any certificate a network attacker presents. Turn it on only where a proxy you control terminates TLS.

**MaxContentCharacters** — Gets or sets how many characters of extracted text reach the caller.

A rendered page of five megabytes fills an agent's context window before it can read a word. Zero means no ceiling; anything above it is cut and the cut is stated in the text itself. The markup is never cut: a ceiling on it would end the document in the middle of a tag and silently lose every link past there, which is exactly what a browser-backed search engine reads the markup for.

**MaxDegreeOfParallelism** — Gets or sets how many pages may be open at once across all hosts.

The only ceiling there is, and that is the change made on 2026-09-14. There used to be two — this one, which sized the workers, and `MaxBrowsers`, which sized the pool — and the effective parallelism was the smaller of them, which at the defaults was ONE while this said three. A setting that promises what another setting silently withdraws is worse than no setting.

What it costs is memory, and the number to raise it by is measurable rather than guessable. Measured on 2026-09-14 by `probes/contexts`: on stock Playwright the first page costs 284 MB and every further one 70, with nothing degrading up to sixteen; on our own browser image, on a machine emulating another architecture, 178 MB a page. Run that probe against the image a deployment actually uses and divide the memory it can spare. Four is a default that fits a small container, not a recommendation.

Across all hosts is the whole of it. Reading many pages of one site — a crawl, or a batch of links from one domain — is paced by `Web:Politeness:MaxConcurrentPerHost` instead, and raising this alone only adds workers that queue at the same per-host gate.

**MaxRetryAttempts** — Gets or sets how many times a transient failure is retried.

**NavigationTimeoutMs** — Gets or sets the navigation budget in milliseconds.

**RememberSiteFor** — Gets or sets how long what a site left with us is kept when nobody goes back to it.

A bound on memory rather than on the cookies themselves: each cookie carries its own expiry inside the state, and the browser drops the expired ones when the state is restored. What this decides is how long a host nobody has visited keeps taking up room. Zero switches the memory off entirely, and then every visit arrives as a stranger — which is what this library did until 2026-09-20.

**RepeatsRefusals** — Gets or sets whether a refusal that names no gap is one of the failures worth repeating.

On, because a wall is often a fingerprint and the next attempt draws a different one — that is what makes a retry worth making at all, and a test holds the crawl to it. Off for a deployment whose target closes over FREQUENCY, where every further request only confirms the refusal and spends the budget.

MEASURED on 2026-09-20 on these defaults: a page that answers costs one request, one turn at the gate and 14 ms; a wall costs four of each and 17.7 seconds of backoff. On a host paced at thirty seconds those four turns are two minutes for one page, so a site that refuses everything turns a forty-minute crawl of some two hundred and sixty pages into one of sixty-five. That is the fourteenth item on the consumer's list, and it is a setting rather than a new default because which of the two a site is cannot be known from here — the engines of this toolkit, whose refusals are measured to be about frequency, do not repeat them at all.

**RetryDelay** — Gets or sets the base delay between retries.

**Sandboxed** — Gets or sets whether Chromium keeps its sandbox.

Scraping opens documents chosen by someone else, which is the exact threat the renderer sandbox exists for. An earlier default passed `--no-sandbox` and `--disable-web-security`, so a renderer exploit on any scraped page ran with the privileges of the host process. Turn this off only where the container already provides isolation and cannot offer the kernel features Chromium needs.

**SectionName** — The configuration section this binds from.

**WaitForNetworkIdle** — Gets or sets whether to wait for network activity to settle after the DOM is ready.

Off by default. Any site with ads, analytics beacons, long polling or a websocket never reaches network idle, so waiting for it spent the entire navigation budget on every such page and then reported a timeout — the slowest possible way to produce nothing.


## ServiceCollectionExtensions

Registers the browser pool, the stealth layer and the two readers.

**AddBrowserPool** — Registers the browser pool on its own, for a caller that drives Playwright directly.

**AddScraping** — Registers scraping, binding every option class from configuration.

Calls the core's own registration first, so a consumer that only wants pages says one thing. The robots source is registered here rather than in the core, because reading `robots.txt` needs a transport and this package is the one that has one.

**AddScraping** — Registers scraping configured in code.


## SiteListing

What a site said when it was asked for its own list of pages.

`HasMore` is carried rather than inferred from the count, because a list that filled the ceiling exactly and a list that overflowed it are different facts and only the second one means the crawl stopped short of the site.

**HasMore** — Whether the site listed more than the ceiling allowed to be taken.

**IsPublished** — Gets whether the site offered a list at all.

**None** — A site that publishes no list worth reading.

**Pages** — The addresses to read, in the order they are to be read.


## SitemapIndex

Builds a site's list of pages from the sitemaps it points at.

The list is read even where `robots.txt` is not obeyed: permission and self-description are different questions, and a site that offers an index is worth taking up on it either way. Where the file names no sitemap, `/sitemap.xml` is tried, because that is where one usually is.


## SnapshotFrame

How much of a page a picture takes in.

**Screen** — What a visitor sees on arriving: the screen, and nothing below it.

**WholePage** — The page from top to bottom, as far as `TallestPage` reaches.


## StealthScript

Aligns what scripts can observe with the identity the context was opened under.

Every patch aligns what the browser already exposes and none invents what it does not. That rule is not tidiness: measured on 13 September 2026 against a patched Firefox that reports `navigator.webdriver` as `false` natively, the same recipe returned results through bare Playwright and was answered with `/sorry/` through this toolkit, twice around a passing control. The script was the difference. It redefined `webdriver` unconditionally, which replaced a native getter with one whose `toString` announces the spoof — the very state the patched build exists to avoid — and it declared `deviceMemory`, `navigator.connection` and `window.chrome` on a Gecko browser that has none of them. A browser answering Chrome questions with a Firefox engine is read in one line.

Three earlier defects made the script worse than injecting nothing. It restored the original `getParameter` without its receiver, so every WebGL query that was not one of the two spoofed parameters threw `Illegal invocation`. It added canvas noise as `+= Math.random() * 0.1` to a `Uint8ClampedArray`, where anything below half a unit rounds back to the original byte, so no pixel ever changed. And every patch ran in one unguarded block, so the missing `navigator.permissions` of a non-secure context aborted the script and silently dropped every patch below it — including the `webdriver` override the whole exercise exists for.


## Visits

Rents a browser, builds the context an identity implies and returns a page ready to be shown to a site.

Ownership is the delicate part: the lease and the context are acquired one after the other, and anything that throws in between would strand a browser in the pool for the life of the process. Both are released here on any failure, and afterwards they belong to the returned `IVisit`.

**OpenAsync** — *no summary*

The context is new every time — a context is what holds one page's work — but it is no longer EMPTY every time: what the host left on the last visit goes back into it, and what this visit ends with is kept for the next. A challenge cleared once is then cleared for as long as the cookie it gave lives, which is the difference between a returning visitor and a stranger who is challenged again.
