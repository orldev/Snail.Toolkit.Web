# Snail.Toolkit.Web.Scraping.Agent

*Generated from the compiler's documentation by `dotnet run --project gen -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

The public surface of this package: 14 types. Everything else it contains is internal and is described under [Decisions](../decisions/index.md) where it carries a measurement.

## AgentOptions

Settings for walking a page through an errand.

**SectionName** — The configuration section these settings bind to.

**Settle** — How long the page is left alone after the last move before it is read.

A submit usually replaces the results in place rather than navigating, so there is no load state to wait for and nothing that says the answer has arrived. Everything else in an errand is held to a measured consequence; this is the one pause that cannot be, which is why it is a setting rather than a constant.


## Effect

What must become true for a step to count as having happened.

This type is the whole point of the package. Measured on a live registry on 2026-09-11: filling a field through Playwright's `fill` set its value — the field read back exactly what was typed — and the site never learned of it, because its suggestions hang off key events that a value assignment does not raise. Worse, typing key by key raised them on one run and not on the next, with identical code. A step that only reports whether it executed is therefore worthless: success and silence look the same. A step reports whether the site reacted.

`Pattern` stays a plain selector, URL fragment or phrase rather than becoming a `Spot`. A consequence is watched for, never acted on, and the failure that made addressing worth a type of its own was about naming the field a move lands in — an element that is watched is one the site has just produced, and it is named by whatever the errand already knows it by.

**Within** — How long the consequence is waited for before the step is called fruitless.


## EffectKind

The kinds of consequence a step can be held to.

**Appears** — An element matching a selector became visible.

**Request** — The page asked the site something, matching a pattern in the request URL.

**Text** — The rendered text of the page came to contain a phrase.

**Vanishes** — An element matching a selector stopped being visible.


## Errand

A page to open and the moves to make on it.

An errand is data, not code, so a new site is a new description rather than a new release — the same reasoning that put source descriptions in files elsewhere in this stack.


## IPageAgent

Walks a page through an errand and returns what it says afterwards.

**PerformAsync** — Opens the errand's address, makes every move on it, and reads the resulting page.


## ISiteCalls

Asks a site's own service the way its page would, and hands back what it answered.

Separate from `IPageAgent` because the answer is a different thing. A walkthrough ends in a rendered page and is reported as one; this ends in whatever the service returned — usually JSON — and putting that into a page's text would be a forgery, dressing a payload up as something a reader saw.

**SendAsync** — Opens the page, mints what the service demands, sends the request and returns its body.


## Mint

A credential the page issues to itself before its own service will answer.

This describes the invisible reCAPTCHA v3 pattern, which is what a registry measured on 2026-09-11 uses: the page calls `grecaptcha.execute(key, {action})`, receives a token and appends it to the request. No human is asked anything — the token exists only because a browser ran the site's own script, which is why such a service cannot be reached over plain HTTP and is not the same thing as a picture code meant to stop automation outright.

`Action` is not decoration: that registry validates it against the route being called, issuing `search_fedresurs` for one endpoint and `search_notary` for the other, and a token minted under the wrong action comes back as "captcha not passed" even though it minted perfectly well.

**Parameter** — The query parameter the token is appended as.

**Within** — How long the page is given to issue the token.


## ServiceCollectionExtensions

Registers the page agent and everything it drives.

**AddPageAgent** — Registers the agent together with the scraping stack it walks pages with.

**AddPageAgent** — Registers the agent with the scraping stack configured in code rather than from settings.


## SiteCall

A request to a site's own service, made from inside one of its pages.

`Path` is relative on purpose: the request goes out from the page, so it carries that page's origin, cookies and whatever clearance it was granted on the way in. That is the whole point — the service answers a visitor, not a stranger.

**ContentType** — The content type of `Body`.

**Method** — The HTTP method the service expects.

**Mint** — The credential the page has to issue first, when the service demands one.

**Settle** — How long the page is left to finish loading its own scripts before it is asked to mint.


## SiteCalls

Opens a page and lets it ask its own service on our behalf.

The request is issued by `fetch` inside the page rather than by an HTTP client, because everything the service checks — origin, cookies, the token the page minted — belongs to the page and cannot be reproduced from outside. Measured on a registry on 2026-09-11: the same body sent over plain HTTP is refused outright, while from inside the page it is answered in full.

The identity, the site's ruling, its turn and the retry are `IAttempts`'s. Measured on 2026-09-12 before that existed: of five runs against a live registry through this class, four answered in eleven or twelve seconds and one died on a navigation timeout that never reproduced.


## Spot

Where on a page a move lands.

A selector is not always the honest way to name a field. Measured on a live court search on 2026-09-12: the placeholder that identified its search field is written in by the site's own script, so it was present on one load and absent on the next, and three walkthroughs in a row addressed a field that was not there. The label beside that field is in the markup every time.

There is no implicit conversion from a string, on purpose. Which of the two ways a step names its element is exactly the fact that was wrong in those three runs, so it is stated at every call site rather than inferred from the shape of an argument.

**Css** — The element a CSS selector matches.

**Labelled** — The field a visible label names.


## SpotKind

The ways a step can name the element it acts on.

**Css** — A CSS selector, matched as written.

**Label** — The visible text of the label that names a field.


## Step

One move in a walkthrough, and the consequence it is held to.

**Attempts** — How many times the move is made before the step is given up on.

Two by default because a live widget was measured reacting to one run and ignoring the next under identical code. Repeating a fruitless move is cheap; reporting a walkthrough as done when the site never noticed is not.

**Expected** — What the site must do in response; a step with none is taken on trust.

**Ready** — How long the element is waited for before the move is attempted at all.

Kept apart from `Attempts` because repeating a move is the wrong cure for an element that is not on the page yet, and because falling back on the browser's navigation budget — tens of seconds — means spending it twice to discover that an address matches nothing. A step that runs out of this budget reports that the element never appeared, which asks for a different repair than a site that was there and ignored the move.

**Rhythm** — The pause between keystrokes, which is also what makes typing look typed.

**Value** — The text to type, or the option text to match when picking.


## StepKind

The kinds of move a walkthrough is made of.

Kept small on purpose, and grown only against a measurement. It began as three — type, pick, click — on the claim that every widget reduces to typing into a field, choosing what the field offered, and pressing the thing that submits. Measured against five search engines on 2026-09-12, the last third of that claim is false: on two of them the submitting element does not exist, and on a third a click on it sends nothing. Those forms are submitted by a key, which is why `Press` is here.

Waiting is still not among them, because a wait is not a move: it is the `Effect` a move is expected to produce. Ticking a box is not among them either — a checkbox and a radio are pressed, so `Click` already says it — and neither is clearing a field, which has not yet been needed by any form this package has been pointed at.

**Choose** — Choose an option of a real `select` element by the text shown for it.

Separate from `Pick` because a native list is not markup that can be pressed: the browser draws it outside the page, so clicking an `option` is unreliable where it works at all. The option is named by its visible text rather than by its value, because the value is an internal code and the text is what a person reading the form would choose. The list need not be on screen: a site that draws its own dropdown hides the native one behind it, and the hidden one is still what the form submits.

**Click** — Press an element.

**Pick** — Choose the offered option whose text matches.

For the lists a site draws itself — a suggestion dropdown is markup like any other, and it is pressed. A real `select` element is not: that one is `Choose`.

**Press** — Press a key while the element has the focus.

The key travels in `Value` and defaults to Enter, which is the one that matters: a search form whose only submit is the return key cannot be walked through at all without this.

**Type** — Type into a field one key at a time.
