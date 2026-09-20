# Snail.Toolkit.Web.Search.Browser.Agent

*Generated from the compiler's documentation by `dotnet run --project docs/generator -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

The public surface of this package: 3 types. Everything else it contains is internal and is described under [Decisions](../decisions/index.md) where it carries a measurement.

## Google

Asks Google through a real browser.

Google is why this package exists, and it is asked the way a person asks: the home page, the query typed into the field a key at a time, then the return key. Measured on 2026-09-13 from one address, a fresh context per arm and twelve minutes between them: typed through the form it returned seven results, asked at its own results address it answered `/sorry/`, and typed again it returned seven results. The path in is therefore not decoration, and it cannot be expressed as a link — which is why this engine lives beside `BrowserSearch` rather than inside the package that only knows addresses.

The consent wall was not met from that address and so is not in the errand. A deployment that meets it adds a step for it; a step for an element that is usually absent would fail every ordinary run instead.

Everything that is not Google-specific — the outbound filter, the ceiling, telling a refusal from an unreadable page — stays in `BrowserSearch`. What is left here is one errand, one parser, and the request that turns a wrapped link into the address it wraps.

**ProviderName** — The name this provider is registered and configured under.


## GoogleOptions

Which Google front end is asked.

**Domain** — Gets or sets the host the query is sent to, without a scheme.

A country front end answers differently from the international one, so this is a deployment decision rather than a constant. Interface language and region are not settings here: they come from the identity the page is fetched under, which is the only way they cannot contradict the user agent beside them.

**FollowDelay** — Gets or sets the gap left between asking where two wrapped links lead.

Every result on a Google page is a wrapper, so one query is followed by one request per link, and those requests are not paced by `Web:Politeness:HostDelays`: the gap configured there is the one between two searches, and at the two minutes this engine is given a seven-link answer would take a quarter of an hour. Measured on 2026-09-14 from one address: six wrappers followed in turn at this interval were all answered with the address they wrap. Following them faster, or at once, was not measured — so this is the interval known to work rather than the shortest one that does.

**SectionName** — The configuration section this binds from.


## ServiceCollectionExtensions

Registers the search providers that have to be walked through their own page.

**AddGoogle** — Adds Google, walked through its own form by the page agent.

Pulls in search and the agent rather than assuming them, so a consumer that wants this one engine says one thing. Both registrations are idempotent, and a deployment that also wants the link-driven engines adds `AddBrowserSearch` beside this — the aggregator asks every provider it is given.
