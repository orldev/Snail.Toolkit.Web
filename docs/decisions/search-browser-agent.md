# Snail.Toolkit.Web.Search.Browser.Agent

*Generated from the compiler's documentation by `dotnet run --project gen -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

8 decisions recorded in this package, each naming the day it was measured. Declarations a consumer cannot see are marked *internal*: they are here because the reasoning is, not because the type is reachable.

## Google.AnsweredAsync

`2026-09-12` · *internal*

`Press` carries no key because it defaults to the return key, and the return key is the only thing that submits this form: measured across five engines on 2026-09-12, two offer no submitting element at all and a third ignores a click on the one it has.


## Google

`2026-09-13`

*Asks Google through a real browser.*

Google is why this package exists, and it is asked the way a person asks: the home page, the query typed into the field a key at a time, then the return key. Measured on 2026-09-13 from one address, a fresh context per arm and twelve minutes between them: typed through the form it returned seven results, asked at its own results address it answered `/sorry/`, and typed again it returned seven results. The path in is therefore not decoration, and it cannot be expressed as a link — which is why this engine lives beside `BrowserSearch` rather than inside the package that only knows addresses.

The consent wall was not met from that address and so is not in the errand. A deployment that meets it adds a step for it; a step for an element that is usually absent would fail every ordinary run instead.

Everything that is not Google-specific — the outbound filter, the ceiling, telling a refusal from an unreadable page — stays in `BrowserSearch`. What is left here is one errand, one parser, and the request that turns a wrapped link into the address it wraps.


## ServiceCollectionExtensions.Paced

`2026-09-13` · *internal*

*Gives this engine a gap measured in minutes unless the deployment named its own.*

The default gap is one second, which is a rate this engine answers with a challenge. Measured on 2026-09-13 from one address: four queries spaced two to four minutes apart, each on a clean context, were answered with results, and the shortest interval that passed was two minutes. Faster was never measured to work, so two minutes is the floor written here rather than a rate known to be safe — a deployment that asks more often should expect the refusal that frequency buys.

Named after the configured front end rather than hard-coded, so a country domain is paced too, and only when the deployment has not set its own gap for that host.


## Google.ResolvedAsync

`2026-09-14` · *internal*

Every result Google shows is a wrapper, so this is where a page of results becomes a page of addresses. The list is capped here rather than by the caller because each link costs a request: measured on 2026-09-14 the page carried six results and six wrappers, and resolving links the caller would discard would be paid for in requests to an engine that answers frequency with a challenge.

A result whose address is already absolute is left alone and costs nothing — the older `/url?q=` form unwraps in the parser without asking anyone.


## Google.DestinationAsync

`2026-09-14` · *internal*

*Asks the engine where one wrapped link leads, without going there.*

The redirect is deliberately not followed and the body never read: the answer is the `Location` header alone, and following it would fetch every result page nobody has asked for yet. Measured on 2026-09-14, the wrapper answers `302` with the address to a client carrying no cookies, no referer and no session, which is why this is a plain request rather than something the browser has to make.

A wrapper that answers anything else leaves the result exactly as the page gave it, and the relative address it still carries is dropped by the outbound filter. A link nobody can follow is not a result, and a page where none of them can be followed is reported by the empty-answer warning for what it is.


## Google.Unwrapped

`2026-09-14` · *internal*

*Unwraps the `/url?q=` redirect Google served on some paths.*

The claim is dated on purpose. This said Google "still" served that form, and the sentence became false without a single edit to the code: as of 2026-09-14 the engine wraps results in `/goto` instead, whose query is encrypted and cannot be unwrapped here at all — `ResolvedAsync` has to ask. The older form still appears on some paths and costs nothing to read, so it stays.


## GoogleOptions.FollowDelay

`2026-09-14`

*Gets or sets the gap left between asking where two wrapped links lead.*

Every result on a Google page is a wrapper, so one query is followed by one request per link, and those requests are not paced by `Web:Politeness:HostDelays`: the gap configured there is the one between two searches, and at the two minutes this engine is given a seven-link answer would take a quarter of an hour. Measured on 2026-09-14 from one address: six wrappers followed in turn at this interval were all answered with the address they wrap. Following them faster, or at once, was not measured — so this is the interval known to work rather than the shortest one that does.


## ServiceCollectionExtensions.Following

`2026-09-14` · *internal*

*Adds the client that asks where a wrapped link leads without going there.*

Redirects are off because the redirect is the answer: with the handler following it the address would be spent fetching the result page and the header carrying it would be gone. Cookies are off because the wrapper was measured on 2026-09-14 to answer a client that has none, and a session sent where it is not needed is one more thing to be recognised by.

Deliberately not `AddSearchClient`. That one holds the per-host gate, and this engine's gate is the two minutes set just above — a seven-link answer would take a quarter of an hour to resolve through it, because the gate paces searches and these are the links of one search. The gap between two follows is `FollowDelay` instead, and unlike the ceiling it is a rate measured to work.

It leaves by the configured exit all the same, which it did not until 2026-09-14: a bare handler was built here and the engine's own address saw the search arrive from the proxy and the link resolved from somewhere else moments later. The link it is resolving is an encrypted blob that engine issued to that session, so it is the one party able to join the two addresses, and it is handed the join.
