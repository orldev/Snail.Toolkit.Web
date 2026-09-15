# Snail.Toolkit.Web.Scraping.Agent

*Generated from the compiler's documentation by `dotnet run --project gen -- docs`. Editing this file by hand is lost on the next build; edit the remark in the source instead.*

11 decisions recorded in this package, each naming the day it was measured. Declarations a consumer cannot see are marked *internal*: they are here because the reasoning is, not because the type is reachable.

## Effect

`2026-09-11`

*What must become true for a step to count as having happened.*

This type is the whole point of the package. Measured on a live registry on 2026-09-11: filling a field through Playwright's `fill` set its value — the field read back exactly what was typed — and the site never learned of it, because its suggestions hang off key events that a value assignment does not raise. Worse, typing key by key raised them on one run and not on the next, with identical code. A step that only reports whether it executed is therefore worthless: success and silence look the same. A step reports whether the site reacted.

`Pattern` stays a plain selector, URL fragment or phrase rather than becoming a `Spot`. A consequence is watched for, never acted on, and the failure that made addressing worth a type of its own was about naming the field a move lands in — an element that is watched is one the site has just produced, and it is named by whatever the errand already knows it by.


## Mint

`2026-09-11`

*A credential the page issues to itself before its own service will answer.*

This describes the invisible reCAPTCHA v3 pattern, which is what a registry measured on 2026-09-11 uses: the page calls `grecaptcha.execute(key, {action})`, receives a token and appends it to the request. No human is asked anything — the token exists only because a browser ran the site's own script, which is why such a service cannot be reached over plain HTTP and is not the same thing as a picture code meant to stop automation outright.

`Action` is not decoration: that registry validates it against the route being called, issuing `search_fedresurs` for one endpoint and `search_notary` for the other, and a token minted under the wrong action comes back as "captcha not passed" even though it minted perfectly well.


## SiteCalls

`2026-09-11`, `2026-09-12`

*Opens a page and lets it ask its own service on our behalf.*

The request is issued by `fetch` inside the page rather than by an HTTP client, because everything the service checks — origin, cookies, the token the page minted — belongs to the page and cannot be reproduced from outside. Measured on a registry on 2026-09-11: the same body sent over plain HTTP is refused outright, while from inside the page it is answered in full.

The identity, the site's ruling, its turn and the retry are `IAttempts`'s. Measured on 2026-09-12 before that existed: of five runs against a live registry through this class, four answered in eleven or twelve seconds and one died on a navigation timeout that never reproduced.


## Moves.MoveAsync

`2026-09-12` · *internal*

*Makes one move on the element the step names.*

`Press` defaults to Enter because that is the case it exists for: measured against five search engines on 2026-09-12, two of them offer no submitting element at all and a third ignores a click on the one it has. `Choose` goes through the browser's own selection rather than through a click, since a native list is drawn outside the page and its options cannot be pressed; and it is made without the browser's check that the list can be seen, because the lists worth choosing from are mostly hidden — a site that draws its own dropdown (PrimeFaces, Select2 and their kind) keeps the native `select` clipped to a pixel behind it, and that is the element the form submits. The German register's form was the measurement, on 2026-09-12: its court list is a hundred and fifty items in a scrolling panel with a filter box, and going through the widget — a click to open the panel, a click on the item — failed once in two runs when the panel did not open in time; choosing on the hidden list is exact and needs no panel.


## PageAgent.ReadAsync

`2026-09-12` · *internal*

*Reads the page the walkthrough ended on, once it is established that it is that page.*

The document is inspected for a refusal here as well as on arrival, because a walkthrough can be shown the door by the very move it was written to make. Measured against a search engine on 2026-09-12: every step succeeded, the site accepted the submit, began loading the results and then redirected to a challenge — and the agent, having checked only the landing page, reported success and handed back two thousand characters of "prove you are not a robot" as the answer to the errand.


## Scene

`2026-09-12` · *internal*

*What the page looked like at the moment a step gave up on it.*

A fruitless step used to report only which move produced nothing, which is the question the errand already answers; the one left open is what the page held instead. Answering it by hand on 2026-09-12 took five throwaway scripts — how many elements the address matched, whether any were visible, what the page offered instead, whether a request went out at all — and every one of those answers is in reach of the agent at the instant it fails.


## Setbacks

`2026-09-12` · *internal*

*Names the step that produced nothing, and shows the page it produced nothing on.*

The state of the page is gathered here rather than left to the caller because by the time an outcome is read the browser is gone. Answering "what was on the page instead" by hand took five throwaway scripts on 2026-09-12, all of them asking what the agent already had in front of it.


## Spot

`2026-09-12`

*Where on a page a move lands.*

A selector is not always the honest way to name a field. Measured on a live court search on 2026-09-12: the placeholder that identified its search field is written in by the site's own script, so it was present on one load and absent on the next, and three walkthroughs in a row addressed a field that was not there. The label beside that field is in the markup every time.

There is no implicit conversion from a string, on purpose. Which of the two ways a step names its element is exactly the fact that was wrong in those three runs, so it is stated at every call site rather than inferred from the shape of an argument.


## Spots

`2026-09-12` · *internal*

*Resolves where a step lands, by selector or by the caption that names the field.*

Playwright's own `GetByLabel` is tried first and is right whenever the markup binds a label to its field. It is not enough on its own: measured on a live registry on 2026-09-12, the caption is a `label` element carrying no `for` attribute and no field inside it — the field sits in a sibling container — so that call matched nothing while the first field following the caption matched exactly one. The last candidate drops the requirement that the caption be a `label` at all, since plenty of forms caption a field with a plain `div`.


## StepKind

`2026-09-12`

*The kinds of move a walkthrough is made of.*

Kept small on purpose, and grown only against a measurement. It began as three — type, pick, click — on the claim that every widget reduces to typing into a field, choosing what the field offered, and pressing the thing that submits. Measured against five search engines on 2026-09-12, the last third of that claim is false: on two of them the submitting element does not exist, and on a third a click on it sends nothing. Those forms are submitted by a key, which is why `Press` is here.

Waiting is still not among them, because a wait is not a move: it is the `Effect` a move is expected to produce. Ticking a box is not among them either — a checkbox and a radio are pressed, so `Click` already says it — and neither is clearing a field, which has not yet been needed by any form this package has been pointed at.


## Setbacks.ExplainedAsync

`2026-09-13` · *internal*

*Why the step at this position produced nothing.*

The document is inspected for a refusal first, because a step stops producing anything the moment the site shows the door, and the two answers call for opposite reactions: a challenge is `Blocked` and worth another identity, while `Empty` is final and worth none. Measured against Google on 2026-09-13: the errand typed the query, the site accepted the submit and began loading the results, then redirected to a reCAPTCHA — and the outcome came back as a step that produced nothing, so the one fact worth acting on was left in the prose while the kind a caller branches on said the opposite. The arrival and the finished page are each checked by the agent itself; this is the third door, and it was the one standing open.
