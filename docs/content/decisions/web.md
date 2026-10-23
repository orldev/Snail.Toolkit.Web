# Snail.Toolkit.Web

*Generated from the compiler's documentation by `dotnet run --project docs/generator -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

26 decisions recorded in this package, each naming the day it was measured. Declarations a consumer cannot see are marked *internal*: they are here because the reasoning is, not because the type is reachable.

## PolitenessOptions.HostDelays

`2026-09-12`

*Gets or sets the hosts that are pressed more gently than the rest, each with its own smallest gap.*

One gap for every host is the wrong shape for a client that talks to a search engine and a regional newspaper in the same minute: the engines measured on 2026-09-12 closed over frequency, one of them after three answers in an evening, while a site with a sitemap does not mind a page a second. A key matches the host itself and every host beneath it, so `example.com` covers `www.example.com` too, and the most specific key wins. A site's own `Crawl-delay` still applies on top — both are floors.


## BlockDetector

`2026-09-13`, `2026-09-15`, `2026-10-04`

*Matches the markers challenge and consent pages leave in the document or in the final URL.*

The URL is inspected as well as the body because the most common refusals are redirects: Google answers with `/sorry/` and sends European visitors to a consent host, and neither page carries any of the markup markers. An earlier version watched the body alone, so a refusal arrived as a successful fetch whose parse found nothing, and the failure was recorded as "empty" rather than "blocked". The generic words `captcha` and `blocked` are deliberately absent from both lists: an article that discusses captchas would match them, and a false block is worse than a missed one — it burns a proxy and a retry.

That rule was written for the markup and broken by the URL beside it, where the bare word stood until 2026-09-13: any address carrying it — `/blog/how-captcha-works` — was called a refusal, and because a refusal is transient the reader then spent three identities proving it again. A signal belongs here only when the words are a refusal wherever they appear, which is why what is left are whole paths and hosts.

THE LAST SIGNAL WAS ADDED ON 2026-09-15 FROM A MEASUREMENT, and it is the shape to copy. An engine's own wall answered `202` — a success status — with 14 228 bytes reading "Unfortunately, bots use DuckDuckGo too. Please complete the following challenge to confirm this search was made by a human. Select all squares containing a duck". NOT ONE existing signal matched: no Cloudflare marker, no recaptcha widget, and the word that appears sixty-seven times on that page — `anomaly` — is exactly the bare word this list refuses to carry, since an article about anomaly detection would match it. What was taken instead is the one sentence that is a refusal wherever it appears, prose rather than infrastructure: the asset paths beside it are the vendor's and will be renamed, the sentence has to keep meaning what it says.

A VENDOR'S NAME IS A BARE WORD TOO, and the list carried one until 2026-10-04. Every page of Wikipedia names the captcha it would show an editor in the settings MediaWiki inlines into the document — `"wgConfirmEditCaptchaNeededForGenericEdit":"hcaptcha"` — so the substring `hcaptcha` called the whole encyclopedia a wall: an article that had loaded in full with status 200 came back as `Blocked: markup:hcaptcha`, and because a block is transient the reader spent its identities proving it again. Measured through the MCP server built on this toolkit, on the first two articles it was asked for. What matches now is the widget, the same way reCAPTCHA is matched: `h-captcha` as the class of the element a visitor has to solve, and not the hidden `h-captcha-response` field beside it.


## ContentBudget.Apply

`2026-09-13`

*Applies the budget to text that has already been through one, naming the length it really had.*

A crawl budgets what the reader budgeted, because reading one page and reading a site are worth different amounts of text. Applying a second ceiling to the first one's output produced a marker that described the first cut rather than the page: measured on 2026-09-13, nine hundred characters came back announced as "60 of 164", and a model reading that believes it saw a third of a short page rather than a fifteenth of a long one. The earlier announcement is stripped before the cut rather than after, so the ceiling can never land in the middle of it and leave half a sentence claiming a length.


## Link.Of

`2026-09-13`

*Reads an address into its comparable form.*

Anything that is not an absolute http or https address is kept verbatim rather than rejected. A provider that hands back a relative or malformed href is still reporting something, and dropping it here would lose the fact silently — `IsAbsolute` is where the caller answers it.

Credentials survive into `Address` and into nothing else. Every other form of a link is read by somebody: `Canonical` is the key a crawl deduplicates by and the value the reader puts on the `url.full` span tag, and `Text` is what a log line prints. Until 2026-09-13 a password written into an address reached all three, so one link with basic credentials published them to the tracing backend and to every log that named the link.


## ServiceCollectionExtensions.BindOnce

`2026-09-13`, `2026-10-12`

*Binds a settings section the first time it is asked for, and does nothing after that.*

`TryAdd` guards the services; nothing guarded the settings. Binding appends to a `List` rather than replacing it, so every module that reaches the core's own section added the configured entries again: measured on 2026-09-13, a consumer calling `AddPageAgent` beside `AddBrowserSearch` got one configured proxy three times and passed `--lang=en-US` to Chromium twice.

A marker per settings type is what makes the second call free. `ServiceCollectionDescriptorExtensions` deduplicates only descriptors that name an implementation type, and a bound section is an instance, so it can never be one of those.

WHAT IS BOUND HERE IS READ ONCE. Every consumer of these settings takes `IOptions` and keeps `.Value` from its constructor, so a change on disk reaches a running process only through a restart. That is a decision rather than an omission, and it is worth stating because the alternative is half a decision: several of these numbers are not re-readable at all without inventing a behaviour for the change — a semaphore cannot be resized while callers wait on it, and a Polly pipeline is assembled once per process by design. A library where some settings reload and others silently do not is harder to reason about than one where none do, and nothing measured so far asks for the first.

THE BINDING IS THE CALLER'S, so that it is written where the settings type is known. The configuration binding generator replaces a `Bind` call with code of its own only where the type is concrete; inside this generic method it could not (SYSLIB1104), the reflection binder ran, and in a native app every setting was silently left at its default. Measured on 2026-10-12 by the audit's native probe.


## Gate

`2026-09-14` · *internal*

*One host's turn-taking: how many at once, how long between, and until when it is left alone.*

Both instants are held as ticks and moved only forwards, by a compare-and-exchange that keeps the later of the two. They used to be `DateTimeOffset` properties written under no lock at all, which cost two things at once. A rest was lost outright: `Rest` compared and then assigned, so a caller carrying a short gap could read the field before a longer rest was written and store its own afterwards — measured on 2026-09-14 by sixty-four simultaneous callers, five to eight rounds in five hundred ended with the host open after a site had asked for thirty seconds. And a reader could in principle see half of one write beside half of another, since twelve bytes are not written at once, which invents a rest rather than losing one.


## PolitenessOptions.LongestRest

`2026-09-14`

*Gets or sets the longest rest a site may ask for and still be waited out.*

A site that answers `429` with `Retry-After` has given a final answer for the next several seconds, and a caller told to come back later than this is better served by hearing so at once than by spending its whole budget waiting to be refused again. Measured on 2026-09-14 against a fixture that refuses by frequency: a gap of three seconds was retried three times and answered in nine, inside the default search budget of thirty; a gap of thirty seconds, inside a budget of twenty, made one request, sat out the entire budget and ended in a cancellation that named neither the status nor the gap. Ten seconds keeps the first family retried and cuts off the second, which provably cannot fit.

It never shortens the rest itself — the gate holds the host for as long as the site asked, whatever this says. What it decides is only whether this caller waits for it or is told now.


## Remembered

`2026-09-14` · *internal*

*What is kept per host, and the forgetting that keeps it from growing for as long as the process runs.*

Two maps in this package answer questions about hosts — how fast one may be asked, and what its `robots.txt` said — and both had the same defect for the same reason: the hosts of the web are not a finite set, and neither map ever removed an entry. They were given the same cure on 2026-09-14 and it was written twice, which is how the cure becomes the next defect.

The subtle part is the part worth having once. Forgetting happens on the arrival of an UNKNOWN key, because that is the only moment the map grows; it is throttled to a quarter of the window, because a sweep reads the whole map and doing that per new key would be quadratic over a crawl; and the throttle is claimed with a compare-and-exchange, so many callers arriving together produce one sweep rather than one each.

What counts as no longer worth keeping is the caller's to say, because the two maps disagree about it: a pacing gate may only be dropped when nobody is inside it and it is holding nobody back, while a cached file may be dropped the moment no answer could come from it any more.


## Fanout.EndedAsync

`2026-09-15`, `2026-09-20` · *internal*

*Waits for the producer to finish, and stops waiting if it will not.*

Tying the producer to the enumeration is what keeps it from blocking forever on a channel nobody reads, and awaiting it here is how that tie is honoured — a consumer that leaves early should not walk away from work still holding a lease. What it must not do is make leaving conditional on the work agreeing to stop. Cancellation has already been asked for; work that does not watch its token would otherwise hold the caller for as long as it likes, and the caller has said it no longer wants the answer.

The grace period is long enough that an orderly stop is always awaited and short enough that a disorderly one is not mistaken for progress; the work is then left to finish on its own, where its exception is observed rather than raised at a caller that has already gone.

WHOSE CARELESSNESS THIS GUARDS AGAINST CHANGED ON 2026-09-15, and the paragraph is rewritten rather than left standing because it had become untrue. This bound was added while `Fanout` was public, and the reason given was that the work would be a stranger's; the type is internal now, so there is no stranger.

That paragraph went on to say that neither caller that exists could produce the failure this prevents, and on 2026-09-20 it was MEASURED FALSE: the search handed its attempts a token of its own and threw away the one given here, so a caller that broke out of the loop waited the full grace while a slow engine ran on. The search now listens to this token as well. The bound is what caught it — the caller waited five seconds rather than for as long as the engine liked.

It is kept, and the reason is no longer a supposition: what it guards against is OUR OWN caller, and one of ours has already been that caller. Nothing about this type makes a third one expensive to add, and nothing about it warns that the work handed over must watch its token — the requirement lives here, in prose, where an author reaching for bounded parallelism will not look. The cost of keeping it is a timer that elapses only when a caller has made that mistake. That is a smaller price than the failure, which is silent: a consumer that has already left the loop, still held.


## HostPoliteness.Rest

`2026-09-15`

Two steps — find the gate, then write the rest on it — and until 2026-09-15 the host could stop being remembered between them, which is not a shorter rest but none at all: the next caller built a gate that had never heard of the refusal and went straight at a site that had just asked to be left alone.

What answers it is in `Remembered`, not here: an entry younger than the forgetting window is never swept, so a gate made a moment ago is still the gate everyone finds. A confirm-and-retry loop around these two lines was written first and then TAKEN OUT, and the reason is recorded here because it is the obvious thing to propose again. MEASURED on 2026-09-15 by removing each cure separately: at the most eager forgetting the settings now permit — one millisecond — the residency rule alone brings sixty-four callers over fifty rounds to zero violations, and removing the loop while keeping residency changes nothing across five runs. It defended only `ForgetHostAfter` of zero, where no residency rule can hold because a gate is stale the instant it is made, and which start-up validation now refuses. A retry loop on the path every page takes, justified by no test that can tell it apart, is a worse trade than the rule that is doing the work.


## Markup

`2026-09-15`, `2026-09-20`

*Parses a page's markup, and refuses markup nested deeper than a page really is.*

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


## PolitenessOptions.GapFor

`2026-09-15`

*The gap named for this host by its own key or by the nearest one above it, when one is named at all.*

The walk is the rule `HostDelays` states, written once rather than in each caller: the host is tried whole and then shortened one label at a time, so `news.example.com` looks for `news.example.com`, then `example.com`, then `com`. A name that merely ends the same way is not a parent, because the cut is always at a dot.

Nothing rather than `MinDelayPerHost` when no key covers the host, because that is the question a second caller had to ask: a package pacing its own engine has to tell "nobody named a gap here" from "the deployment named exactly the default", and a floor cannot say which it is. Measured on 2026-09-15 against the first consumer of the Google package: a deployment naming `google.com` was read as naming nothing, because the check there was an exact-key lookup for `www.google.com`, and both keys ended up standing with the more specific one winning — so the deployment's setting was overridden by a package that had decided it was absent.

Case is compared here rather than left to the dictionary's own comparer: the default instance ignores it, and an instance arriving from configuration binding is not promised to.


## Remembered.Sweep

`2026-09-15`, `2026-09-20` · *internal*

*Drops what is no longer worth keeping, and never what has only just arrived.*

The residency check is the fix for a defect this class had from the day it was written, and it is worth stating because the predicate looks like it already covers it and does not. `Of` hands back a value with nothing written to it yet — a gate holding nobody, an entry whose read has not started — which is exactly the state `isStale` is looking for. The next arrival of an unknown key therefore swept it away before its owner had written anything, and the owner's write then landed on an object the map no longer held.

MEASURED on 2026-09-15: sixty-four callers, fifty rounds, `ForgetHostAfter` zero — 3196 occasions on which two callers were inside one host at once, each correctly holding the only slot of a different gate. `MaxConcurrentPerHost` is the setting that exists to make that impossible.

The window is measured from the last time the entry was ASKED FOR, not from when it was made, and that difference was read on 2026-09-20. Measured from creation, the rule protects a value made a moment ago and nothing else — so a host last visited an hour ago and visited again NOW was swept out from under the caller it had just been handed to, whose rest then landed on an object the map no longer held and whose successor built a second gate for the same host. Being handed to a caller is the evidence that somebody has been to this host lately, which is the whole of what the window asks.

A value younger than `forgetAfter` is kept whatever the predicate says, because that is what the name already promises. It closes every configuration but one: a forgetting of zero is eager by request, and no rule here can hold against it — a gate is stale the instant it is made. Start-up validation refuses that setting instead.


## Rule

`2026-09-15` · *internal*

*One `Allow` or `Disallow` line, matched the way the site wrote it.*

The expression is built from a file the site being read wrote, so it is hostile input. A pattern of alternating literals and wildcards — `/a*b*c*d*e*` becomes `^/a.*b.*c.*d.*e.*` — backtracks catastrophically on a path that nearly matches it.

That used to be answered with a hundred-millisecond deadline per match, and the deadline was the wrong shape for the problem. It bounds ONE rule, while `IsAllowed` runs every rule in the group and nothing bounds how many rules a file may hold: MEASURED on 2026-09-15, two hundred lines of the pattern above cost twenty seconds to decide ONE address, and a file of a hundred thousand — three megabytes, an ordinary response body — would cost the better part of three hours. Multiplying a bound by an unbounded count is not a bound.

`NonBacktracking` answers it instead, and answers it completely: `Translate` emits nothing but escaped literals and `.*`, which that engine supports in full, and it matches in time linear in the length of the path whatever the pattern looks like. The catastrophe is not caught any more, it cannot be written. The deadline is gone with it, and so is the rule that ran out of time counting as no match.


## Rule._matcher

`2026-09-15` · *internal*

*The expression this rule matches by, or nothing when it needs no expression at all.*

Most of a real `robots.txt` is literal prefixes — `/admin/`, `/cgi-bin/` — and a prefix is what `Translate` turns into `^` followed by an escaped literal, which is `StartsWith` written the long way round. Deciding that here rather than building an expression to discover it matters because the expression is built at parse time, once per rule per named agent, for every site that answers `robots.txt` — whether or not a single address of it is ever checked.

MEASURED on 2026-09-15: thirty thousand literal rules cost 2.3 s to parse as non-backtracking expressions and 40 ms as prefixes. Non-backtracking is the slower engine to construct and the only safe one to run, so the answer is to construct it only where a wildcard means something has to be.


## TextExtraction.Append

`2026-09-15` · *internal*

*Writes the document's text, with a line break on both sides of every block element.*

An explicit stack rather than the call stack, and that is not a matter of taste. The walk used to recurse once per level of nesting, and nothing bounds how deep a document goes: HtmlAgilityPack builds whatever the bytes describe. MEASURED on 2026-09-15 against HtmlAgilityPack 1.13.0, on a thread carrying the 1 MB stack the thread pool gives its workers — which is where a crawl runs every read, through `ForEachAsync` — six thousand levels returned and eight thousand ended the process with a `StackOverflowException`.

Eight thousand `div` elements is forty kilobytes of body, which any site can serve and some generate by accident. That exception cannot be caught, runs no `finally` and is not a failed page: it takes the process and every other page in flight with it, which is the one failure in this library that no amount of retrying survives.

A `null` on the stack is the line break owed to a block element whose children have all been written. It is pushed before that element's children, so it is popped after them. The break BEFORE the element is written at once: without it, text standing directly ahead of a block — `<div>Introduction<p>First</p></div>` — ran into it as «IntroductionFirst».

The break owed to a `pre` also says so, and that is how the walk knows which text is the author's: everything between the element and its break is inside it, at any depth. The text nodes under a `pre` used to be found by an XPath query over the whole document first.


## HostPoliteness.DelayAsync

`2026-09-20` · *internal*

*Waits until this host opens, however far off that is.*

The wait is taken in stretches because `Delay` refuses anything past about forty-nine days. MEASURED on 2026-09-20: a site answering `Retry-After` of a hundred days made every later caller to that host meet an `ArgumentOutOfRangeException` naming a timer, and the host stayed broken for as long as the process ran. A rest that long is a site to be left alone, not a defect to be raised at whoever arrives next — and a caller that will not wait it out cancels, which is the answer it already had.


## Spread.ShouldReadAhead

`2026-09-20`, `2026-09-14` · *internal*

*Whether more should be read before choosing, counting only the hosts that could be used now.*

The clock here is the system's, where the rest of the toolkit reads an injected `TimeProvider` since 2026-09-20, and that is deliberate: this decides what to OFFER a worker next, never whether it may go, so being wrong costs a worker queueing at a gate — which is what it did for every item before this existed. Nothing observes the difference, so nothing could test it.

Counting hosts rather than usable hosts is what the first version did, and it reduced the whole thing to nothing in the case it was written for: with three workers and three sites resting at the head of the list, three hosts were in hand and two of them shut, so at most one worker could be given anything. Measured on 2026-09-14 — two pages read in the window where eighteen were waiting.


## TextExtraction

`2026-10-04`

*Turns markup into readable text with its paragraph structure intact.*

Two defects are fixed here against the version this came from. `SelectNodes` answers `null` when nothing matches, and the result was dereferenced unconditionally, so any document without a script, style, noscript or frame threw `NullReferenceException`. And every newline was collapsed into a space, which destroys the paragraph boundaries a language model needs to tell a heading from a sentence.

THAT SECOND FIX OVERSHOT, and stood until 2026-10-04: every newline of the SOURCE was kept, as though a line break in markup were a line break on the page. It is whitespace. A sentence written across three source lines with a link in the middle came back as three lines, a navigation bar as a column of single words — measured through the MCP server built on this toolkit, on a page of the PostgreSQL manual, where roughly half the lines of the text were one word long. What ends a line now is what ends one in a browser: a block element, on both of its sides. Whitespace inside a text node is one space, except in preformatted text, which is the one place a line break in the source is the author's.

Found by the same reading: the page's `title` was part of its text, and since it is not a block it ran into the first paragraph — «Example DomainThis domain is for use…». The title is answered by `Title`; here the head is noise like a script is.


## BlockDetector.Inspect

`2026-10-12`

Nearly every page is clear, and finding that out took eleven case-insensitive scans of the whole document and a regular expression after them. One pass over all the words at once answers "nothing here" first — measured on 2026-10-12 by the audit, 0.17–0.21 of the time — and only a page that carries one of them is asked again signal by signal, so the signal named is the one the list names first, as before.


## IdentityRotation.Built

`2026-10-12` · *internal*

*The identity of the fingerprint at `index`, leaving by `exit`.*

Everything an identity says is derived from its fingerprint alone, and it was derived again on every request — a regular expression, three chains of comparisons and a split of the languages: measured on 2026-10-12 by the audit, about 300 ns and 1 021 bytes a request, for the same answer each time. The identity is now shaped once per fingerprint and handed out again, with the exit set when a proxy is in use. It is shaped anew whenever the fingerprint's values are no longer the ones it was shaped from, since a fingerprint is a settings object anyone holding it can change.

Since every identity of one fingerprint now carries the same list of languages, the list is handed out read-only: as an array, a caller casting it back could have rewritten the languages of every identity after its own.


## Link.Reduced

`2026-10-12` · *internal*

*Takes the comparable form from the parsed address's own parts, or builds it where that is not sure.*

Building it with a `UriBuilder` and reading the built address back parsed every link twice and split its query into an array, a sequence and a joined string, for every link of every page, every sitemap entry and every search result: measured on 2026-10-12 by the audit, 0.47 of the time and 52 % of the bytes once the parts are concatenated instead. Concatenating is only sure where the second parse could not have changed anything, so whatever it could change — credentials, a host outside lower-case ASCII or written as IPv6, http on the port of https, a query opening with a second question mark, a path that trims down to nothing or to a dot — is still built the long way, and both ways answer the same form.


## SiteRules.IsAllowedAsync

`2026-10-12`

Asked on every attempt at every page, and for every page of a site but the first the rules are already read: the answer then comes from them at once. It used to build the origin's address, a closure and an async state machine each time — measured on 2026-10-12 by the audit, with the identity beside it, 2 437 bytes a request where 328 do. Whatever is not read yet, still being read or out of date takes the road it always took.


## TextExtraction.Clean

`2026-10-12`

*Answers the readable text of a document already parsed, and leaves the document as it was.*

The text, the title and the links of one page used to be read from a parse each — two for every page read and four for every page a crawl follows — and parsing is most of what any of them costs: measured on 2026-10-12 by the audit, the parse alone was 0.28–0.43 of the text and title together. One parse can answer all of them only if none of them changes it, and the text used to: it removed the scripts, the styles and the whole head from the document before walking it, and the head is where the canonical address, the base and the robots directives of the links are written. The walk now passes over those elements instead of removing them, and answers the same text.


## LinkExtraction.Names

`2026-10-13` · *internal*

*Answers whether a space- or comma-separated list of relations names `relation`.*

Read in place: the list used to be split, each entry lower-cased into a string of its own and gathered into a list, for every anchor of every page, to answer one yes or no. Each entry is still trimmed of white space and compared after lowering each character by the invariant culture, which is what `ToLowerInvariant` did to it, so a relation matches exactly the spellings it matched then. A comparison ignoring case would not: measured over every character on 2026-10-13, lowering turns the Kelvin sign into `k` and an ordinal comparison ignoring case does not match the two.


## Fanout.OverAsync

`2026-10-23` · *internal*

*Streams every result `work` hands over for an item, each the moment it is handed over.*

For work with more than one thing to say about an item, each of which is news as soon as it is known. The search is the reason it exists, since 2026-10-23: when an engine fails in a place of the round it asks another in that place, and the failure belongs to the caller before the second engine has answered — `StreamAsync` promises each attempt as it finishes, and handing the two over together would have broken that for exactly the attempts a caller most needs to see early.

The work keeps its slot of the degree of parallelism for as long as it runs, so a second answer for an item is bounded the same way the first one was rather than taking a slot of its own. An item that throws hands over the result `failed` gives for it, after whatever it had already handed over. Everything else is the other overload's, which is this one with a single result per item.
