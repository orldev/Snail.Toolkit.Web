# Snail.Toolkit.Web

*Generated from the compiler's documentation by `dotnet run --project docs/generator -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

The public surface of this package: 35 types. Everything else it contains is internal and is described under [Decisions](../decisions/index.md) where it carries a measurement.

## BlockDetector

Matches the markers challenge and consent pages leave in the document or in the final URL.

The URL is inspected as well as the body because the most common refusals are redirects: Google answers with `/sorry/` and sends European visitors to a consent host, and neither page carries any of the markup markers. An earlier version watched the body alone, so a refusal arrived as a successful fetch whose parse found nothing, and the failure was recorded as "empty" rather than "blocked". The generic words `captcha` and `blocked` are deliberately absent from both lists: an article that discusses captchas would match them, and a false block is worse than a missed one — it burns a proxy and a retry.

That rule was written for the markup and broken by the URL beside it, where the bare word stood until 2026-09-13: any address carrying it — `/blog/how-captcha-works` — was called a refusal, and because a refusal is transient the reader then spent three identities proving it again. A signal belongs here only when the words are a refusal wherever they appear, which is why what is left are whole paths and hosts.

THE LAST SIGNAL WAS ADDED ON 2026-09-15 FROM A MEASUREMENT, and it is the shape to copy. An engine's own wall answered `202` — a success status — with 14 228 bytes reading "Unfortunately, bots use DuckDuckGo too. Please complete the following challenge to confirm this search was made by a human. Select all squares containing a duck". NOT ONE existing signal matched: no Cloudflare marker, no recaptcha widget, and the word that appears sixty-seven times on that page — `anomaly` — is exactly the bare word this list refuses to carry, since an article about anomaly detection would match it. What was taken instead is the one sentence that is a refusal wherever it appears, prose rather than infrastructure: the asset paths beside it are the vendor's and will be renamed, the sentence has to keep meaning what it says.


## BlockVerdict

Whether a fetched page is the page that was asked for, and what gave it away when it is not.

Detection used to answer with a bare `Boolean`, so a log could record that a page was refused but never which wall it hit — the one fact that says whether to rotate the proxy, slow down, or accept a consent form.

**Blocked** — The verdict of a page that was refused, and what matched.

**Clear** — The verdict of a page that showed no sign of a block.

**IsBlocked** — Whether the site answered with a challenge or a refusal.

**Signal** — The indicator that matched, for logs and metrics.


## BudgetedText

Text that has been through a `ContentBudget`.

The flag is carried beside the text rather than left to be inferred from the marker, so a caller can branch on the fact — fetch the rest, widen the budget, warn the reader — without parsing prose.

**IsTruncated** — Whether anything was dropped.

**OriginalLength** — How long the content was before the budget was applied.

**Text** — The content, carrying `Marker` when it was cut.


## ContentBudget

A ceiling on how much text may travel back to a caller, with any cut stated in the text itself.

Neither engine this was assembled from had a limit at all: a five-megabyte page went back whole, and the caller that hurts most is an agent, whose context window it fills before it can read a word. Silent truncation is worse than none — a model given half a document with no sign of the cut answers confidently about a page it has not seen — so the marker is part of the text and names both lengths.

**Apply** — Applies the budget, appending the marker when anything was dropped.

**Apply** — Applies the budget to text that has already been through one, naming the length it really had.

A crawl budgets what the reader budgeted, because reading one page and reading a site are worth different amounts of text. Applying a second ceiling to the first one's output produced a marker that described the first cut rather than the page: measured on 2026-09-13, nine hundred characters came back announced as "60 of 164", and a model reading that believes it saw a third of a short page rather than a fifteenth of a long one. The earlier announcement is stripped before the cut rather than after, so the ceiling can never land in the middle of it and leave half a sentence claiming a length.

**Marker** — The words that announce a cut. Present verbatim in every truncated result.

**MaxCharacters** — Gets how many characters of content survive.

**Of** — Reads a budget of a given size.

**Unlimited** — A budget that never cuts.


## Fingerprint

One fingerprint the toolkit may present, as configuration supplies it.

Fingerprints are configuration rather than a compiled array. An earlier version froze four Chrome 123 and 124 user agents into a `private static readonly` field; those versions shipped in April 2024, and a browser claiming them today is more suspicious than one claiming nothing. Refreshing the list must not require a release of this library.

**AcceptLanguage** — Gets or sets the `Accept-Language` header value.

**Locale** — Gets or sets the BCP 47 locale, such as `ru-RU`.

**Timezone** — Gets or sets the IANA time zone, such as `Europe/Moscow`.

The default is a real zone rather than `UTC`, and that is not cosmetic. Measured on 13 September 2026 from one residential address, a fresh browser profile per arm and one variable changed between them: Google answered `/sorry/` on `UTC` and returned results on both `America/New_York` and `Europe/Moscow`, the second disagreeing with the exit address. A zone that contradicts the address is therefore not what is read — people travel and use VPNs — but `UTC` is what containers and servers default to, and consumer machines do not run it.

**UserAgent** — Gets or sets the user agent string. Platform, brand and version are derived from it.


## HostPoliteness

Holds a per-host gate: a concurrency limit and a minimum gap between requests.

The delay is measured from the moment the previous request to that host finished, so a slow page does not accumulate a debt of queued requests that all fire the instant it completes. The gap is the configured default for most hosts and a longer one for those named in `HostDelays`, decided once, when the host's gate is created.

**Dispose** — *no summary*

The semaphores are deliberately left alone. A turn in flight releases its own when the page it paces finishes, and that is after shutdown by definition — a crawl is cancelled while pages are still open. Disposing them underneath answered the caller's `await using` with an `ObjectDisposedException` naming a semaphore, which is the last thing anyone reads while looking for why the run stopped. Nothing here holds a handle worth reclaiming: `SemaphoreSlim` allocates one only for `AvailableWaitHandle`, which nobody asks for. What is left to do at shutdown is forget the hosts.

**OpensAt** — *no summary*

A host never spoken to opens at once: it has no gate yet, and creating one to answer a question about what to do next would fill the map with hosts nobody went on to visit.

A host with every slot taken has no knowable opening — it depends on a request still running — and is answered with one gap from now. That is an estimate and it is the useful one: it ranks a host that is merely busy ahead of one resting longer than a gap, and behind one standing free, which is the whole of what a chooser needs from it.

**Rest** — *no summary*

Two steps — find the gate, then write the rest on it — and until 2026-09-15 the host could stop being remembered between them, which is not a shorter rest but none at all: the next caller built a gate that had never heard of the refusal and went straight at a site that had just asked to be left alone.

What answers it is in `Remembered`, not here: an entry younger than the forgetting window is never swept, so a gate made a moment ago is still the gate everyone finds. A confirm-and-retry loop around these two lines was written first and then TAKEN OUT, and the reason is recorded here because it is the obvious thing to propose again. MEASURED on 2026-09-15 by removing each cure separately: at the most eager forgetting the settings now permit — one millisecond — the residency rule alone brings sixty-four callers over fifty rounds to zero violations, and removing the loop while keeping residency changes nothing across five runs. It defended only `ForgetHostAfter` of zero, where no residency rule can hold because a gate is stale the instant it is made, and which start-up validation now refuses. A retry loop on the path every page takes, justified by no test that can tell it apart, is a worse trade than the rule that is doing the work.


## IBlockDetector

Recognises challenge, consent and refusal pages returned in place of the requested one.

**Inspect** — Judges a document and the URL the fetch ended on.


## IIdentityRotation

Produces the identity a single visit is made under.

**For** — The identity this host always sees, drawn once and answered the same way forever after.

A site that let us in remembers WHOM it let in: a cleared challenge hands back a cookie bound to the address and the user agent that solved it — Cloudflare's `cf_clearance` is the common one. Keeping that cookie while drawing the next identity from the round makes it worthless, and makes the visit stranger than one with no cookie at all: a returning visitor whose browser changed between requests.

The pick is a stable hash of the host rather than a remembered map, so it survives a restart and costs no memory, and the exit is picked the same way for the same reason — a clearance cookie is bound to the address that earned it.

**Next** — Takes the next identity, with its proxy already chosen.


## ILinkExtraction

Reads the addresses a document points at, and the one it claims for itself.

**CanonicalOf** — Reads the address the document declares as its own, when it declares one.

The same content is served at several addresses on nearly every real site — a print view, a page parameter, a session id. `Link` reduces the forms of one address; only the page itself can say that two different addresses are one document, and this is where it says so.

**From** — Resolves every `href` a robot may follow against the address the page was read from.


## IPoliteness

Paces requests so that no single host sees a burst.

**EnterAsync** — Waits for this host's turn and holds it until the returned handle is disposed.

Limits are per host, so parallelism across a list of many sites is unaffected while one site never sees more than it allows. Request rate is the signal a site reads first, and the engines previously had no answer to it at all — every worker hit the same host as fast as the transport could load.

A gap the site asked for never shortens the configured one. Both are floors, so the wait is the longer of the two: a site asking for one second cannot talk a careful caller down to it.

**OpensAt** — The soonest this host could let a caller in, as far as is known.

For choosing what to do next, never for deciding whether it may be done: `EnterAsync` remains the only thing that grants a turn, and an answer here can be out of date by the time it is read. Being approximate is what makes it useful — a caller about to wait at a shut gate can spend the same moment on a site that is open, and where the guess was wrong it waits at the gate exactly as it would have anyway.

An instant rather than a yes or no, and that distinction was measured rather than reasoned. A yes-or-no answer calls a host busy for the twenty milliseconds of its own gap and a host resting for five seconds by the same word, so a chooser with nothing open in front of it picks between them at random — and picks the five seconds a third of the time. What is being ranked is how long the wait would be.

Without it a run can stop while there is nothing wrong with it: workers handed addresses regardless of whose gate is shut all sit out a refusal together while a hundred other sites go unread.

**Rest** — Holds a host back for a gap it has just asked for.

`EnterAsync` asks for a gap on behalf of one request; this one is the site asking, and it binds every later caller rather than the one that was told. A `429` answered to one worker is not a fact about that worker, and the other two hitting the same host would otherwise carry on into it.

Written down where the pacing already lives, so the wait costs no thread and no retry of its own: the next attempt enters the gate like any other and finds it closed. A longer hold already in place is kept — being asked twice never shortens the wait.


## IProxyRotation

Hands out the configured proxies in turn.

**For** — The exit this host always leaves by, or nothing when none is configured.

The other half of a steady identity: a challenge cleared from one address is cleared for that address, so a host kept to one browser has to be kept to one exit as well.

**Next** — Takes the next proxy, or `null` when none are configured.


## IRobotsSource

Reads the `robots.txt` of one origin.

The core parses and matches robots rules but never fetches them: downloading is the one part that needs a transport, and binding the core to an HTTP client for it would put a client — and eventually a browser — into every consumer of this package. Implementations live next to the transport that already exists.

**ReadAsync** — Reads the rules an origin publishes, through `proxy` when one is given.

The fetch goes through the same proxy the rest of the work uses. Reading `robots.txt` directly would announce the real origin of a crawl that every other request takes pains to route elsewhere. A site that cannot be asked is answered with `AllowAll` rather than an exception: RFC 9309 suggests failing closed on a server error, but doing so on a transient 502 would stop a crawl silently and look exactly like a bug.


## ISiteRules

Answers what a site's published rules say.

**DelayAsync** — Answers the gap the site asks to be left between fetches, when it asks for one.

Answers nothing where `robots.txt` is not obeyed, and reads no file to find out: a caller that switched the rules off did not ask for the traffic that answering would cost.

**IsAllowedAsync** — Checks the site's rules, reading `robots.txt` on first use and caching the answer.

**RulesAsync** — Reads the whole file, for the sitemaps and the pace a caller cannot get from permission.

This answers even where `robots.txt` is not obeyed. Obedience decides whether a refusal is honoured; it says nothing about the site's own index of its pages, which is worth having either way.


## ISitemapSource

Downloads one sitemap document.

Declared here and implemented by whichever package owns a transport, for the same reason robots rules are: the core describes what it needs and stays free of an HTTP client. A sitemap is also the one document that must not be read with a browser — a rendered XML file comes back as the browser's own tree view, markup and all, and parses as nothing.

**ReadAsync** — Reads the document at `address`, answering an empty one when it cannot.


## ITextExtraction

Reduces a document to the text a reader would see.

**Clean** — Strips scripts, styles and frames and returns the readable text.

**Title** — Answers the document's own name — its `<title>`, or its first heading when it has none — or `null` when it has neither.

Kept apart from `Clean` because the title is not part of what a reader sees on the page and is lost with the head otherwise. A search result and an index entry both need the page's name, and the first line of body text is a poor stand-in for it.


## IdentityOptions

Which identities and proxies the toolkit rotates through.

Bound through the standard options pattern and validated when the host starts, so a proxy that cannot be read refuses to bring the process up instead of throwing from the middle of a fetch.

**Fingerprints** — Gets or sets the fingerprints to rotate through. Empty uses the built-in set.

**Proxies** — Gets or sets the proxies to rotate through, as `[scheme://][user:password@]host:port`.

Parsed once when the host starts. Parsing used to happen on every page, inside a lock, and threw `FormatException` from a hot path when an entry was malformed.

**SectionName** — The configuration section this binds from.


## IdentityRotation

Builds one coherent identity per visit from the configured fingerprints.

Every observable trait is derived from a single user agent, so the platform a script reports, the platform client hints announce and the operating system the user agent names cannot disagree. The values used to come from two independent generators and routinely contradicted each other.

The fingerprints are handed out in turn, the way `ProxyRotation` hands out addresses and for the same reason: a random pick repeats itself in bursts, and with three configured a run of three picks the same one about one time in nine. A burst of identical fingerprints from one address is the pattern rotation exists to avoid, so drawing at random undid the feature while looking like it.


## Link

One address, reduced to the form both engines agree on.

This is the seam between search and scraping rather than a convenience. Search deduplicates results by address and scraping applies politeness and robots rules by address; the moment the two normalise differently, the same page counts as one link on one side and two on the other. The same page arrives as http and https, with and without a trailing slash, and carrying whichever campaign parameters the referrer attached, so equality is decided on `Canonical` alone.

**Address** — Gets the parsed address, or `null` when the text is not an absolute http address.

**Canonical** — Gets the form two providers would agree on.

**IsAbsolute** — Gets whether the text parsed as an absolute http or https address.

**Of** — Reads an address into its comparable form.

Anything that is not an absolute http or https address is kept verbatim rather than rejected. A provider that hands back a relative or malformed href is still reporting something, and dropping it here would lose the fact silently — `IsAbsolute` is where the caller answers it.

Credentials survive into `Address` and into nothing else. Every other form of a link is read by somebody: `Canonical` is the key a crawl deduplicates by and the value the reader puts on the `url.full` span tag, and `Text` is what a log line prints. Until 2026-09-13 a password written into an address reached all three, so one link with basic credentials published them to the tracing backend and to every log that named the link.

**Text** — Gets the address as it was written, less any credentials it carried.


## LinkExtraction

Reads a document's links as absolute addresses.

Everything a page links to is written relative to something, and the something is not always the address the page was fetched from: a `<base href>` silently moves it, and a reader that ignored that would resolve an entire site one directory off and find nothing.

Anything that is not http or https is dropped rather than reported — `mailto:`, `tel:`, `javascript:` and a bare fragment are all ordinary on a real page and none of them is a page to read. Duplicates are dropped by canonical form, so the twenty links a navigation bar repeats count once.

`nofollow` is obeyed, on the link and on the document, because it governs traffic this library is about to send. `noindex` is not: it asks not to be listed in a search index, and a caller reading one site on purpose is not building one.


## Markup

Parses a page's markup, and refuses markup nested deeper than a page really is.

The parser is QUADRATIC IN NESTING DEPTH, and nothing here bounded it. Measured on 2026-09-15 by `probes/Markup.cs` against HtmlAgilityPack 1.13.0, as the read path pays it — `Clean` plus `Title`, both of which every read goes through:

```csharp
depth     12 →     4.7 ms      depth   2000 →    70 ms
depth    500 →     5.1 ms      depth   5000 →   400 ms
depth   1000 →    16.1 ms      depth  20000 →  5.60 s

```

SIZE IS THE WRONG AXIS, which is what makes this worth a type of its own. A catalogue of 2.3 MB nested twenty deep costs 254 ms; a page of 161 KB nested five thousand deep costs 400. A ceiling on bytes generous enough to admit the first admits the second, and the second is the one that holds a worker for half a second of pure CPU on 161 KB that any site can serve.

The ceiling is the parser's own, checked INSIDE the parse rather than after it, and that is the whole reason this is cheap enough to be a cure: refusing costs 1.4 ms where accepting cost 200, and 1.6 ms where accepting cost 2.79 s. On markup that passes it costs nothing measurable — the first version of that measurement ran the two arms one after the other and reported a 20% tax, which was the second arm running on a warmer process; interleaved by round, the difference changes sign between runs.

WHY 1000 AND NOT A SETTING. Every real page measured sits two orders below it: an article nests twelve deep, a catalogue twenty. The ceiling is not a policy a consumer tunes to taste — nobody wants a page nested a thousand deep read — it is the line past which markup stops being a document, and putting a number like that in configuration invites it to be raised by someone who has met a refusal rather than a page. What a consumer needs instead is to be TOLD, which is why the refusal is named rather than silent. If a real page is ever found above this, the answer to weigh is a setting, and this paragraph is where the argument against one already stands.

THE FOUR SEARCH ENGINES CAME THROUGH HERE ON 2026-09-15, and the question that had kept them out was the one this paragraph named: what a refused results page IS. It is neither of the two answers that were available — not "no results", which is a statement about the query, and not a fault, which is a defect on our side to go and fix — so the search contract grew a third, `SearchFailureKind.Unreadable`, which rests no engine because markup too deep to read says nothing about how often the engine will answer. The engine that fetches over HTTP parses here and maps the refusal itself; the two driven by a browser parse in `BrowserSearch`, which is why that package is named beside the others in Web.csproj.

OPEN TO A CONSUMER SINCE 2026-09-20, which was the thirteenth item on the consumer's list. A caller that reads pages this library never fetched — a bot with ten probes of its own — was choosing between the extraction this package offers, which answers text and links and nothing else, and its own parser, which pays the cost above on every hostile page. The door is the same one the engines use: the ceiling is not a setting, and the refusal arrives as `MarkupTooDeepException` so that a caller can say WHY it read nothing. What it hands back is HtmlAgilityPack's document, so a consumer of this method takes a dependency on that package's shape — which the two search packages already do, and which is the price of asking for a document rather than for text.

**MostNesting** — How deep a page may nest before it is refused instead of read.

**Read** — The parsed document, or a refusal naming the depth it stopped at.

The catch is broad because the parser gives nothing narrower to catch: HtmlAgilityPack 1.13.0 raises a bare `Exception` for this, so the type cannot tell it apart from anything else and neither can its message, which is English prose subject to change. What makes the breadth acceptable is what else `LoadHtml` can do — it reads a string, touches nothing, and is built to accept malformed markup rather than to complain about it. `OutOfMemoryException` is let through because it is a fact about the process rather than about the page, and calling it a nesting depth would send whoever reads the failure looking at the wrong thing.


## MarkupTooDeepException

Raised when a page's markup nests deeper than this library is willing to parse.

The one exception type this toolkit defines, and it is one because every alternative makes an extraction lie. `Clean` answers a string, `Title` a string or nothing, `From` a list — markup that was never read would come back from all three as "this page has no text, no name and no links", which is a statement about the page and not about us. A read that ends in this has not looked at the page at all.

A consumer of the engines never sees it: the page-reading tail turns it into `FailureKind.Unreadable`, which is where an answer of this shape belongs. It reaches only a consumer calling `ITextExtraction` or `ILinkExtraction` with markup of their own.

**MostNesting** — Gets the nesting depth at which reading stopped.


## Outcome

What an operation produced, or why it produced nothing.

Both engines used to answer with a bare `String`, so a captcha wall, a navigation timeout and a genuinely empty document all arrived as an empty result and no caller could tell a site that has nothing to say from one that refused to talk. A successful outcome always carries a value: `Success` refuses `null`, so success and "there is something to read" are the same fact.

**Failed** — Reports a failed operation.

**Failure** — Gets the failure, or `null` when the operation succeeded.

**IsSuccess** — Gets whether the operation succeeded.

**Success** — Reports a successful operation.

**TryGetValue** — Reads the value of a successful outcome.

A false answer states that `Failure` is there, so the failing branch reads it without the null check the compiler would otherwise demand.

**Value** — Gets the value a successful operation produced.

Reading this on a failed outcome is a mistake in the calling code rather than a runtime condition, so it throws instead of handing back an empty value. Use `TryGetValue` where the outcome is open.


## PolitenessOptions

How hard a single site may be pressed.

Considerable effort used to go into fingerprints and none into request rate, which is the signal a site notices first and the one no amount of canvas noise hides. Three parallel workers hitting one host from one address is what gets an address banned; these limits are applied per host, so a crawl spread across many sites keeps its throughput while no single site sees a burst.

**ForgetHostAfter** — Gets or sets how long a host is remembered after the last request to it.

The gate keeps one entry per host and nothing used to remove one, so a crawl across many sites grew it for as long as the process ran. What is worth remembering is exactly what a pacing decision can still depend on: a host nobody has spoken to for longer than this gets the same answer from a fresh entry as from the kept one. Ten minutes is far longer than any gap this class hands out and short enough that a long crawl holds only the sites it is actually working on.

It never shortens a rest. A host still being held back, or with a request in flight, is not forgotten whatever this says.

**GapFor** — The gap named for this host by its own key or by the nearest one above it, when one is named at all.

The walk is the rule `HostDelays` states, written once rather than in each caller: the host is tried whole and then shortened one label at a time, so `news.example.com` looks for `news.example.com`, then `example.com`, then `com`. A name that merely ends the same way is not a parent, because the cut is always at a dot.

Nothing rather than `MinDelayPerHost` when no key covers the host, because that is the question a second caller had to ask: a package pacing its own engine has to tell "nobody named a gap here" from "the deployment named exactly the default", and a floor cannot say which it is. Measured on 2026-09-15 against the first consumer of the Google package: a deployment naming `google.com` was read as naming nothing, because the check there was an exact-key lookup for `www.google.com`, and both keys ended up standing with the more specific one winning — so the deployment's setting was overridden by a package that had decided it was absent.

Case is compared here rather than left to the dictionary's own comparer: the default instance ignores it, and an instance arriving from configuration binding is not promised to.

**HostDelays** — Gets or sets the hosts that are pressed more gently than the rest, each with its own smallest gap.

One gap for every host is the wrong shape for a client that talks to a search engine and a regional newspaper in the same minute: the engines measured on 2026-09-12 closed over frequency, one of them after three answers in an evening, while a site with a sitemap does not mind a page a second. A key matches the host itself and every host beneath it, so `example.com` covers `www.example.com` too, and the most specific key wins. A site's own `Crawl-delay` still applies on top — both are floors.

**LongestRest** — Gets or sets the longest rest a site may ask for and still be waited out.

A site that answers `429` with `Retry-After` has given a final answer for the next several seconds, and a caller told to come back later than this is better served by hearing so at once than by spending its whole budget waiting to be refused again. Measured on 2026-09-14 against a fixture that refuses by frequency: a gap of three seconds was retried three times and answered in nine, inside the default search budget of thirty; a gap of thirty seconds, inside a budget of twenty, made one request, sat out the entire budget and ended in a cancellation that named neither the status nor the gap. Ten seconds keeps the first family retried and cuts off the second, which provably cannot fit.

It never shortens the rest itself — the gate holds the host for as long as the site asked, whatever this says. What it decides is only whether this caller waits for it or is told now.

**MaxConcurrentPerHost** — Gets or sets how many requests may be in flight against one host.

**MinDelayPerHost** — Gets or sets the smallest gap between two requests to the same host.

**SectionName** — The configuration section this binds from.


## Proxied

Builds the handler an outgoing request leaves through, sending it via the configured exit when there is one.

Here rather than in a transport package because every transport needs it and none of them owns it, and it costs the core nothing: `SocketsHttpHandler` ships with the framework, so this adds no package reference. Without it a consumer has two exits at once — the browser honours the configured proxies because its context is built from the identity, while every plain `HttpClient` ignores them. A source reached both ways is then fetched from two different addresses, which is worse than having no proxy at all: half the requests are still refused and the reason looks like the source rather than the route.

The exit is chosen once, when the handler is built, and a pooled handler outlives many requests. With a single proxy that is exactly right; with a list, the HTTP path takes one of them and stays on it rather than rotating per request the way the browser does. Rotation here would mean a handler per request, which throws away connection pooling — a worse trade than the one it fixes.

**Handler** — The handler for one exit, tuned by whatever the caller needs on top of it.

**Handler** — The handler for the next exit of a rotation, or a direct one when none is configured.

**Handler** — The same handler, resolved from the container where a registration hands one over.

The shape `ConfigurePrimaryHttpMessageHandler` asks for, so a named client is proxied by one line at its registration rather than by a factory of the consumer's own.


## ProxyEndpoint

One proxy a request can be routed through.

The scheme is part of `Server` rather than being dropped during parsing. A previous version built the endpoint as `host:port`, so a `socks5://` proxy was silently used as HTTP and traffic went somewhere other than the configuration said.

**IsAuthenticated** — Gets whether the proxy carries credentials.

**Password** — Gets the password, when the proxy requires authentication.

**Server** — The endpoint in the form a transport expects, scheme included.

**ToString** — *no summary*

Credentials are deliberately not rendered: this value reaches logs.

**TryParse** — Reads a proxy written as `[scheme://][user:password@]host:port`.

Credentials are optional. An earlier parser demanded them, so an open proxy such as `http://10.0.0.1:8080` threw `FormatException` from the middle of a fetch. A missing scheme is read as `http`, which is what a bare `host:port` has always meant here. The split on the first colon only is deliberate: a password containing one is common, and splitting on every colon silently truncates it.

**Username** — Gets the user name, when the proxy requires authentication.


## ProxyRotation

Rotates through the proxies parsed once at start-up.

Parsing happens in the constructor rather than per call. An earlier version ran a non-compiled regular expression inside a `lock` on every page, which serialised every parallel worker on the hot path, and threw `FormatException` from inside a retry loop when an entry was malformed. Rotation is a single interlocked increment, so nothing is serialised at all — and it is round-robin rather than random, because a random pick reuses the same address in bursts and that is exactly what gets an address banned.


## RobotsFile

The rules one site publishes in `robots.txt`.

Parsing and matching are kept free of I/O so both are directly testable — the deterministic half of this behaviour previously had no tests at all. Matching follows the usual precedence: the longest pattern that matches wins, and `Allow` beats `Disallow` at equal length.

The file answers three questions, not one. Permission is the famous one, but `Sitemap` is how a site offers its own index of pages — the difference between reading a domain by following links and being handed the list — and `Crawl-delay` is the pace it asks for. Both were parsed away as unknown fields.

**AllowAll** — Rules that permit everything, used when a site publishes none.

**CrawlDelay** — Answers how long the site asks `agent` to wait between fetches.

Null means the site asked for nothing, which is not the same as asking for zero: the caller keeps its own configured delay rather than being told there is none.

**IsAllowed** — Answers whether `path` may be fetched by `agent`.

**Parse** — Reads a `robots.txt` document.

**Sitemaps** — Gets the sitemaps the site points at, in the order it named them.


## RobotsOptions

Whether a site's published rules are obeyed, and how they are kept.

**CacheLifetime** — Gets or sets how long a fetched `robots.txt` stays authoritative.

**IsObeyed** — Gets or sets whether `robots.txt` is fetched and obeyed.

Off by default only because switching it on silently would drop URLs that existing callers expect to be fetched, and a dropped URL looks exactly like a bug. Switching it on is the correct choice for any crawl of sites you do not own.

**LongestCrawlDelay** — Gets or sets the longest `Crawl-delay` that is actually waited between two requests.

A site's own pace is obeyed, and it is obeyed up to a point, because the figure is written by the site being read and nothing else checked it. A `Crawl-delay` of 86400 used to be waited in full: one worker standing in the gate for a day, holding both the host's only slot and one of `Web:Scraping:MaxDegreeOfParallelism`. Four lines like it in four different files stopped a whole crawl, and nothing in the log said so — a worker that is waiting is not failing.

A ceiling rather than the refusal that answers `Retry-After` past `LongestRest`, and the difference between the two is the difference between the headers. `Retry-After` accompanies a refusal, so handing it straight back to the caller passes on something the site actually said; `Crawl-delay` is a pace and refuses nothing, so answering it with a refusal would drop the site from the crawl rather than read it slowly. One minute is far above what any site pacing itself sensibly asks for and far below what stalls a run.

**SectionName** — The configuration section this binds from.

**Timeout** — Gets or sets how long a `robots.txt` fetch may take before it is abandoned.

Applied by whichever transport implements `IRobotsSource`, since only it does I/O.

**UserAgent** — Gets or sets the product token matched against `User-agent` groups.

A browser user agent is not a robots product token, and matching one against these groups would find nothing, so the wildcard group is the honest default.


## ServiceCollectionExtensions

Registers the shared behaviour both engines stand on.

**AddBlockDetection** — Registers challenge and refusal detection.

**AddIdentityRotation** — Registers one identity and proxy rotation shared by every caller.

**AddLinkExtraction** — Registers link extraction.

**AddPoliteness** — Registers the per-host gate and the clock it reads.

Singleton because a gate is only a limit while every caller waits at the same one. The clock is registered beside it rather than taken for granted, so a host that keeps its own `TimeProvider` — a test moving time by hand, a deployment with a clock of its own — is the one this library reads.

**AddSiteRules** — Registers robots rules on top of the transport that can read them.

**AddTextExtraction** — Registers readable-text extraction.

**AddWeb** — Binds the toolkit's configuration sections and registers everything the core provides.

Robots rules are absent on purpose: obeying them means downloading `robots.txt`, and the core owns no transport. Whichever engine is added next registers its own source with `AddSiteRules`.

**AddWeb** — Registers everything the core provides, leaving the settings at their defaults.

For a consumer that configures in code rather than from a file. Validation is registered here rather than beside the binding, so both routes refuse the same impossible settings.

**BindOnce** — Binds a settings section the first time it is asked for, and does nothing after that.

`TryAdd` guards the services; nothing guarded the settings. Binding appends to a `List` rather than replacing it, so every module that reaches the core's own section added the configured entries again: measured on 2026-09-13, a consumer calling `AddPageAgent` beside `AddBrowserSearch` got one configured proxy three times and passed `--lang=en-US` to Chromium twice.

A marker per settings type is what makes the second call free. `ServiceCollectionDescriptorExtensions` deduplicates only descriptors that name an implementation type, and a bound section is an instance, so it can never be one of those.

WHAT IS BOUND HERE IS READ ONCE. Every consumer of these settings takes `IOptions` and keeps `.Value` from its constructor, so a change on disk reaches a running process only through a restart. That is a decision rather than an omission, and it is worth stating because the alternative is half a decision: several of these numbers are not re-readable at all without inventing a behaviour for the change — a semaphore cannot be resized while callers wait on it, and a Polly pipeline is assembled once per process by design. A library where some settings reload and others silently do not is harder to reason about than one where none do, and nothing measured so far asks for the first.


## SiteRules

Caches the rules of every origin visited, one read per origin.

Caching lives here rather than in the transport so that both engines age entries identically: a crawl that re-read `robots.txt` per URL would announce itself twice per page, and one that never re-read it would obey a year-old file.


## SitemapEntry

One page a site lists in its sitemap.

`changefreq` and `priority` are dropped on the way in. Both are hints to a search engine about what to re-read and in what order, neither is binding, and both are routinely wrong — a reader that trusted them would skip pages the site claimed were yearly and had edited that morning.

**Address** — Where the page lives.

**LastModified** — When the site says it last changed, when it says at all.


## SitemapFile

One sitemap document: the pages it lists, or the sitemaps it points at.

A sitemap is why reading a whole domain is usually not a crawl at all: the site hands over the list of its own pages, including the ones no navigation links to, and the reader that already reads a list of addresses does the rest. Following links is the fallback for sites that publish none.

Element names are matched without their namespace. The schema says `sitemaps.org/schemas/sitemap/0.9`, plenty of real sitemaps declare something else or nothing at all, and refusing those would reject exactly the sites whose markup already needs the most patience.

Malformed XML reads as an empty document rather than an exception, which matches how `robots.txt` is read here: a site's own file is input, not a contract, and a caller that gets nothing back reports that the sitemap yielded nothing.

**Empty** — A document that lists nothing, used when a site has no sitemap or an unreadable one.

**IsIndex** — Gets whether this document points at further sitemaps rather than at pages.

**Pages** — The pages a `urlset` lists.

**Parse** — Reads a `urlset` or `sitemapindex` document.

**Sitemaps** — The sitemaps a `sitemapindex` points at, each with the date the index gives it.


## TextExtraction

Turns markup into readable text with its paragraph structure intact.

Two defects are fixed here against the version this came from. `SelectNodes` answers `null` when nothing matches, and the result was dereferenced unconditionally, so any document without a script, style, noscript or frame threw `NullReferenceException`. And every newline was collapsed into a space, which destroys the paragraph boundaries a language model needs to tell a heading from a sentence.


## WebDiagnostics

The activity source and meter every package in this toolkit publishes under.

Engines whose normal failure mode used to be an empty string had no way to be watched: nothing counted blocks, nothing timed a fetch, and a crawl that quietly returned nothing looked identical to one that succeeded. One name is shared across the packages so a consumer subscribes once and sees the whole toolkit; the instruments themselves belong to whichever package measures something.

**Activity** — The source spans are recorded on.

**Meter** — The meter instruments are created from.

**SourceName** — The name to subscribe to for traces and metrics.


## WebIdentity

Everything a site can observe about who is visiting, decided in one place.

This type exists to end a contradiction that defeated a whole stealth layer. The request headers and the injected script used to be produced independently: the script hard-coded `navigator.platform = 'Win32'` and Russian `navigator.languages` while the profile generator picked a macOS user agent and a German locale. The resulting fingerprint belongs to no real browser and is more identifying than sending nothing at all. Every consumer now reads the same identity. Everything a browser alone can show — viewport, device scale, touch — is deliberately absent: this type is shared with an HTTP transport that has no such thing to report, and a field nobody can honour is a lie waiting to be told.

**AcceptLanguage** — Gets the `Accept-Language` header value.

**Brand** — Gets the browser brand for client hints, such as `Google Chrome`.

**DeviceMemory** — Gets the value `navigator.deviceMemory` reports, in gigabytes.

**HardwareConcurrency** — Gets the value `navigator.hardwareConcurrency` reports.

**Languages** — Gets the values `navigator.languages` reports, derived from `AcceptLanguage`.

**Locale** — Gets the BCP 47 locale, such as `ru-RU`.

**MajorVersion** — Gets the major version the user agent claims.

**NavigatorPlatform** — Gets the platform as `navigator.platform` spells it, such as `Win32`.

**Platform** — Gets the platform as client hints spell it, such as `Windows` or `macOS`.

**Proxy** — Gets the proxy this identity is routed through, when there is one.

**SupportsClientHints** — Gets whether client hints apply, which they do for Chromium brands only.

**Timezone** — Gets the IANA time zone, such as `Europe/Moscow`.

**UserAgent** — Gets the user agent string sent and reported to scripts.
