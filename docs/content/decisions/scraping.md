# Snail.Toolkit.Web.Scraping

*Generated from the compiler's documentation by `dotnet run --project docs/generator -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

24 decisions recorded in this package, each naming the day it was measured. Declarations a consumer cannot see are marked *internal*: they are here because the reasoning is, not because the type is reachable.

## ClientHints

`2026-09-11`

*Rewrites the browser's user-agent metadata over CDP so a page reads the identity it was told about.*

Measured on 2026-09-11 against a live site: the context sent `Sec-Ch-Ua: "Google Chrome";v="140"` while the page still read `HeadlessChrome;151` from `navigator.userAgentData`, and the site's analytics carried the headless build away in its `uafvl` parameter. Playwright has no context option for this — `BrowserNewContextOptions.UserAgent` spoofs the user-agent string alone — so the metadata has to be set through `Emulation.setUserAgentOverride`, which only Chromium answers. A header and a script that disagree are a stronger signal than either would be on its own, which is why failing to align is reported rather than passed over.

Chromium only, and the check is on the engine rather than on the exception it would raise: `NewCDPSessionAsync` throws on Firefox and WebKit, and a warning logged on every page of a Firefox run would say nothing anyone can act on.


## CrawlOptions.IsNewestFirst

`2026-09-12`

*Gets or sets whether the pages a sitemap lists are read newest first, by the dates the site gives them.*

A newspaper's sitemap index runs for years. Measured on 2026-09-12: read in index order, a ceiling of five hundred pages filled with whatever the site listed first — two hundred and sixty-four sponsored articles from 2021 — while the news of the week never came up. Newest first opens the most recently dated parts of an index before the rest and takes the most recently dated pages of what they list. A site that dates nothing is read in its own order either way.


## CrawlOptions.Budget

`2026-09-13`

*Gets or sets how long a single crawl may run.*

A crawl that hits this keeps everything it read and says the time ran out, because a partial answer to "what is on this site" is worth having and an exception is not.

Forty minutes rather than ten, decided on 2026-09-13. A page costs eight to nine seconds on the defaults — a second of politeness, one request per host, behaviour simulated — so ten minutes bought about seventy pages against a ceiling of five hundred: the ceiling was decoration and every crawl of a real site ended as `TimeLimit` whatever `MaxPages` said. This is still short of what the ceiling allows, and deliberately so: the two are a budget and a limit, not one number written twice, and a crawl is an operation whose price the caller is meant to see.


## Appetite

`2026-09-14`

*How much of a page the browser is allowed to fetch.*

A site that counts requests rather than pages reads one ordinary page load as a burst, because that is what it is. Measured against Brave on 2026-09-14 from one address, arms alternating and a fresh context each: the full page cost 71 to 73 requests and was answered `429` three times out of three, while the document alone cost one request and was answered `200` three times out of three — carrying all eighteen result blocks, because that engine renders its results on the server.

So this is not a way around anything. Fetching the images, fonts, styles and beacons of a page that will only be parsed is bandwidth nobody wanted and load the site did not have to carry; asking for the document is both lighter on the site and what gets an answer. It stays a choice rather than a default because a page that is driven rather than read needs its scripts: Google's query is typed into a form that does not exist without them.


## Attempts.IsWorthRepeating

`2026-09-14` · *internal*

*Answers whether repeating the work could do better than repeating the refusal.*

A site that names a gap has answered for the next several seconds, and the only question left is whether this caller waits it out. Past `longestRest` it does not: measured on 2026-09-14, a thirty-second gap inside a twenty-second budget made one request, spent the whole budget waiting in the gate and ended in a cancellation carrying neither the status nor the number — while the first answer had already said both. The gate still holds the host for the full gap; what changes is that the refusal reaches the caller at once instead of being sat on.


## BrowserPool

`2026-09-14`, `2026-09-15`

*Keeps one browser alive and hands out permission to have a page open on it.*

A lease used to be a whole browser process, held for the length of a visit, so the number of pages in flight could never exceed the number of processes the pool was willing to run — one, at the defaults. A lease is now one page's worth of permission on a shared browser, and the ceiling is how many pages may be open at once rather than how many processes may exist.

MEASURED on 2026-09-14 by `probes/contexts`, which is the whole reason this is shaped this way. On stock Playwright a browser process with one page costs 284 MB and every further context on it costs 70 — four times cheaper — and opening one takes 133 ms against 340 for a process, with nothing degrading up to sixteen. So processes buy nothing a context does not buy cheaper.

The same probe answered something that removed the choice altogether: against our own browser image the count of browser processes in the container stays at ONE however many connections are made, because that image publishes an already-launched browser. A pool that counted connections was calling them browsers and getting one either way. Keeping several was therefore not a trade between isolation and memory — it was a setting with no effect, and it is gone.

What is given up is stated plainly: a browser that crashes takes every page in flight with it, where several processes would have cost a fraction. Against a browser server that cannot be bought back at any price, and the retry in `IAttempts` is what answers it.

WHAT WAS TAKEN OUT OF HERE ON 2026-09-15, and what deliberately was not. This file was 439 lines and had been the source of a defect three times over. Three of its four jobs came out cleanly and are now `Driver` (start Playwright, open a browser of the configured engine), `Permits` (how many pages at once, and the idle clock) and `Reaper` (close a browser nobody is using). Two of the three historical defects lived in what is now the first and second of those.

THE FOURTH — opening the browser — WAS PROPOSED AND REFUSED, and the reason is written here because it is the obvious next thing to suggest about a class that is still the longest in the package. It reads as a separable job and is not one: what makes it correct is the two things it shares with shutdown, and both are things a second object could only be handed a copy of. The gate it waits on is linked to `_shutdown`, which also releases callers queued in `QueuedAsync` for a page — one cancellation serving two gates, and the remark on `OpeningAsync` says outright that the two are written as a pair and for the same reason. And what decides whether a browser that has finished opening is published or closed is `_isDisposed`, read at the one instant that races shutdown; handed across an object boundary it becomes two facts that have to be kept in step by whoever remembers to, which is the exact shape of the defect that cost a hung test host.

There is a third, smaller reason and it is the one this library cares about most: all three refusals a caller can meet in there name `BrowserPool`. Moved, they would name a type the caller has never heard of — which is word for word the failure the remark on `OpeningAsync` exists to prevent.


## BrowserTraits.For

`2026-09-14`

*The traits this identity reports, the same ones every time it is presented.*

The viewport is chosen by the user agent rather than drawn per visit, which is the correction made on 2026-09-14. A random draw gave one identity a different screen on every visit — the same browser, on the same machine, resized between two page loads — and that is a contradiction of exactly the kind this type was extracted to end. People do resize windows, but the pairing of a screen with a machine is the stable part, and an identity is a machine.

One call answers both the context and the injected script, so a page's window size cannot disagree with what its scripts see. That held before by reusing one draw and holds now by construction.


## IBrowserLease

`2026-09-14`

*Permission to have one page open on the pooled browser, until it is disposed.*

It used to be exclusive use of a whole browser process, which is why the toolkit could never read more pages at once than it ran browsers — one, at the defaults. A context costs a quarter of a process and opens in a third of the time (measured by `probes/contexts` on 2026-09-14), so what is worth rationing is pages rather than processes.

Disposing gives the permission back. Failing to dispose removes it from circulation permanently, so a lease belongs in an `await using` and nowhere else.


## PageFailure.RetryAfter

`2026-09-14`

*Gets the gap the site asked to be left alone for, when it asked for one.*

A `429` is the site saying how often it is willing to be asked, and `Retry-After` is the only part of that answer with a number in it. Until 2026-09-14 nothing here read the header at all, so a refusal by frequency was answered with three more requests on a schedule of our own — the toolkit's reply to "too often" was to ask again, sooner than asked.


## PageFailure.IsRefusal

`2026-09-14`, `2026-09-20`

*Gets whether the site turned the request away, as distinct from failing to answer it.*

A wall, a `429`, a `403` and a `503`: the four shapes in which a site says it will not answer this caller now. The HTTP side of the toolkit has read the same three statuses as a refusal since 2026-09-14; the browser side read only a wall and a `429` until 2026-09-20, so a browser engine answering `403` was a fault — never rested, asked again at every query. What a refusal calls for is asking less, which is the opposite of what a fault calls for, and that is why the two are told apart.


## Reaper

`2026-09-14` · *internal*

*Closes the browser when nothing has used it for a while, and never while anybody is inside.*

It reaches the pool through two questions and nothing else — whether a browser is open, and please close it — which is the whole of the coupling and the reason this could be lifted out while the opening could not. Everything it needs to decide WHETHER to close is in `Permits`: the idle clock and the all-or-nothing take that proves nobody is holding a page.

A pool connected to a browser server never runs one of these. The browser on the other end was launched by somebody else and outlives every connection to it, so closing "the idle browser" would end our connection and reap nothing — MEASURED on 2026-09-14 by `probes/contexts`: the process count in that container stays at one however many connections come and go.


## ScrapingOptions.MaxDegreeOfParallelism

`2026-09-14`

*Gets or sets how many pages may be open at once across all hosts.*

The only ceiling there is, and that is the change made on 2026-09-14. There used to be two — this one, which sized the workers, and `MaxBrowsers`, which sized the pool — and the effective parallelism was the smaller of them, which at the defaults was ONE while this said three. A setting that promises what another setting silently withdraws is worse than no setting.

What it costs is memory, and the number to raise it by is measurable rather than guessable. Measured on 2026-09-14 by `probes/contexts`: on stock Playwright the first page costs 284 MB and every further one 70, with nothing degrading up to sixteen; on our own browser image, on a machine emulating another architecture, 178 MB a page. Run that probe against the image a deployment actually uses and divide the memory it can spare. Four is a default that fits a small container, not a recommendation.

Across all hosts is the whole of it. Reading many pages of one site — a crawl, or a batch of links from one domain — is paced by `Web:Politeness:MaxConcurrentPerHost` instead, and raising this alone only adds workers that queue at the same per-host gate.


## Visit.DisposeAsync

`2026-09-14` · *internal*

The browser goes back whatever the context does. Closing a context reaches the browser server over a socket and fails at the most ordinary moment there is — the connection dropped while the page was open — and until 2026-09-14 that refusal left the lease undisposed. The slot then left the pool for the life of the process, and at the default of one browser that is the whole pool: the next read waits forever on something nobody holds, without an error anywhere to say so.

The refusal is still raised. A context that would not close is worth hearing about — it is the caller's page that is still open somewhere — and swallowing it here would trade a visible failure for a leak.


## BrowserPool.DisposeAsync

`2026-09-15`

Every step here is something that can fail, and none of them may take the rest down with it. The shutdown token is cancelled first because it is what releases a caller already queued for a page; the reaper is awaited for its ending rather than its answer; and the driver and the tokens are released in a `finally`, because a browser that refuses to close would otherwise leak the process that hosts it.

NEITHER GATE IS DISPOSED — not `_opening` here, and not the one inside `Permits`, which is why that type has no `Dispose` to call. A caller released after shutdown would meet a disposed object, and nothing here holds a handle worth reclaiming: `SemaphoreSlim` allocates one only for `AvailableWaitHandle`, which nobody asks for. `Dispose` does not wake anybody waiting on it either — a caller queued behind an opening browser was left on a gate nobody would open again and never returned at all. MEASURED on 2026-09-15: the test host had to be killed.

The rule held for the page semaphore from the start and was not extended to this one, which is exactly the kind of omission a paragraph cannot prevent and a type can. Half of it is now enforced by `Permits` having no way to be disposed; this field is the half still kept by hand, and it is kept here rather than moved because what it guards — publishing the browser — is what shutdown races with.


## Driver

`2026-09-15` · *internal*

*Holds the Playwright installation and opens browsers from it, launched here or connected to elsewhere.*

The installation is started once and kept, because unpacking it is the expensive part and every browser after the first comes from the same one. A start that FAILED is not kept, and that distinction is the whole of this class: a `Lazy<Task<IPlaywright>>` caches the task, so one failure — a machine busy while the driver unpacks, a temporary file that was not there yet — answered every later rent with the same exception until the process restarted. It was named in the code as undemonstrable on 2026-09-15, since `CreateAsync` is static and no test can make it fail. Asking for it through a function rather than by name is what made it demonstrable, and the test that follows failed before this was written.


## BrowserFailure.KindOf

`2026-09-20` · *internal*

*Reads what Playwright said into the kind a caller branches on.*

A broken WebSocket is the browser going away, not a page that would not load. Until 2026-09-20 it read as `Navigation` — a statement about the SITE — and a measurement on 16.09.2026 shows why that matters: one heavy page killed Firefox inside the container, the Playwright server closed with it, and every caller saw `WebSocket error: socket hang up` on whatever page it happened to be reading. A reader looking at that line goes to the site; the thing to look at was the browser.


## Rests

`2026-09-20` · *internal*

*Reads the gap a site asked to be left alone for out of what it answered with.*

Written once and read by everything that drives a browser. Until 2026-09-20 only the page reader read the header: the errand and the call to a site's own service carried the status and never the number, so a service that said "come back in thirty seconds" in the middle of a form told one caller and nobody else — `Attempts` writes the gap into the per-host gate, where it binds every later caller, and it had nothing to write. That is the twelfth item on the consumer's list.

Headers rather than a response, so that the reading can be checked without a browser, and the instant is given rather than taken from a clock, so that a date in the header can be checked at all.


## ScrapingOptions.RememberSiteFor

`2026-09-20`

*Gets or sets how long what a site left with us is kept when nobody goes back to it.*

A bound on memory rather than on the cookies themselves: each cookie carries its own expiry inside the state, and the browser drops the expired ones when the state is restored. What this decides is how long a host nobody has visited keeps taking up room. Zero switches the memory off entirely, and then every visit arrives as a stranger — which is what this library did until 2026-09-20.


## ScrapingOptions.RepeatsRefusals

`2026-09-20`

*Gets or sets whether a refusal that names no gap is one of the failures worth repeating.*

On, because a wall is often a fingerprint and the next attempt draws a different one — that is what makes a retry worth making at all, and a test holds the crawl to it. Off for a deployment whose target closes over FREQUENCY, where every further request only confirms the refusal and spends the budget.

MEASURED on 2026-09-20 on these defaults: a page that answers costs one request, one turn at the gate and 14 ms; a wall costs four of each and 17.7 seconds of backoff. On a host paced at thirty seconds those four turns are two minutes for one page, so a site that refuses everything turns a forty-minute crawl of some two hundred and sixty pages into one of sixty-five. That is the fourteenth item on the consumer's list, and it is a setting rather than a new default because which of the two a site is cannot be known from here — the engines of this toolkit, whose refusals are measured to be about frequency, do not repeat them at all.


## Visit.KeptAsync

`2026-09-20` · *internal*

*Keeps what the site left, while there is still a context to read it from.*

Here rather than in whoever asked for the visit, because the state can only be read while the context is open. A browser that went away in the middle takes it with it, and that is not worth an exception: the visit is over either way and the next one simply arrives as a stranger, which is what every visit did until 2026-09-20.


## IPageSnapshots

`2026-10-04`

*Takes a picture of a page with a real browser.*

The text of a page says what it says; a picture says what it shows — a chart, a map, a layout, a page whose content is drawn rather than written. Added on 2026-10-04 for the MCP server built on this toolkit, where the reader is a model that can look.

A picture is a visit like any other: the same identity, the same politeness gate, the same robots rules and the same judgement of a wall, because it goes through the same `IAttempts` and `IVisits` the page reader does. A site that would refuse the text refuses the picture.


## Crawler.IsSaidAlready

`2026-10-12` · *internal*

*Answers whether this crawl already kept a page saying exactly this.*

Only text long enough to be its own evidence is compared. Two thin pages — a navigation fragment, an empty category — are identical for reasons that have nothing to do with being one document, and collapsing those would lose real pages rather than duplicates.

The digest is taken over the text's own UTF-16 bytes onto the stack and kept as its first 128 bits. It used to be the UTF-8 bytes in a new array, turned into a hex string of 64 characters: measured on 2026-10-12 by the audit, 35 680 bytes and twice the time per page of 20 000 characters, for an answer that is the same — two texts collide in 128 bits of SHA-256 no more often than in 256.


## PageContents.Read

`2026-10-12` · *internal*

*Reads the page's text and title, from one parse of its markup where the extraction is this library's own.*

Each of the two parsed the markup on its own, and the parse is most of what either costs. Another extraction is asked as it always was: what it does with markup, a ceiling on nesting included, is its own.

A search engine's results page is parsed and nothing more. Its text and title were extracted for every query and read by nobody, since an engine reads its results out of the markup: measured on 2026-10-12 by the audit, three parses and the text cost 4.34 MB a page, one parse alone 1.23 MB.


## HttpSitemapSource

`2026-10-13`

*Downloads a sitemap over HTTP, decompressing the gzipped ones.*

Compression is decided by the first two bytes rather than by the file name or the content type. A sitemap is routinely served as `.xml.gz` with no `Content-Encoding`, which no HTTP client decompresses on its own, and just as routinely served gzipped from a name that says `.xml`.

The read is capped. Compressed XML expands by twenty to one and the file is written by the site being read, so an unbounded read is a memory limit set by someone else.

The cap holds on the network too: only the headers are awaited and the body is read under it. Awaited whole, as the client does by default, the body was buffered before the cap was ever consulted — measured on 2026-10-13, an answer of 50 MB cost 95 MB per read under a ceiling of 1 000 bytes. A body that breaks off while it is read fails as an `IOException`, and reads as no sitemap, as a failed request does.
