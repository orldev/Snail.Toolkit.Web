# Getting started

Both engines register the way anything else in this stack does, and read their settings from the `Web:`
section of your configuration.

```csharp
builder.Services.AddSearch(builder.Configuration);      // Snail.Toolkit.Web.Search
builder.Services.AddScraping(builder.Configuration);    // Snail.Toolkit.Web.Scraping
```

```jsonc
{
  "Web": {
    "Politeness": { "MinDelayPerHost": "00:00:01", "MaxConcurrentPerHost": 1 },
    "Scraping": { "BrowserServer": "ws://localhost:3000/" }
  }
}
```

## Settings are read once

Every option class is bound and snapshotted when the host starts, so a change to `appsettings.json` reaches a
running process only when it is restarted. There is no live reload anywhere in the toolkit, and a setting that
appears to have been ignored has usually been edited under a process that read it an hour ago.

That is a decision rather than an omission, and it is worth stating because the alternative is half a
decision: several of these numbers are not re-readable at all without inventing a behaviour for the change — a
semaphore cannot be resized while callers wait on it, and a resilience pipeline is assembled once per process
by design.

## Asking a question

```csharp
var found = await search.FindAsync(["postgresql documentation"]);

foreach (var result in found.Results)
    Console.WriteLine($"{result.Title} — {result.Address}");
```

## Reading a page

```csharp
var outcome = await pages.ReadAsync(Link.Of("https://www.postgresql.org/docs/"));

if (outcome.IsSuccess)
    Console.WriteLine(outcome.Value.Text);
```

## Walking a page

Some pages say nothing until someone fills a form in. `Snail.Toolkit.Web.Scraping.Agent` takes an **errand** —
an address and the moves to make on it — and returns the page that resulted, or names the move that produced
nothing.

```csharp
var outcome = await agent.PerformAsync(new Errand(Link.Of("https://example.test/search"),
[
    new Step(StepKind.Type, Spot.Labelled("Notice number"))
    {
        Value = "2024-001",
        Expected = new Effect(EffectKind.Request, "suggest.json")
    },
    new Step(StepKind.Click, Spot.Css("#look"))
    {
        Expected = new Effect(EffectKind.Text, "is registered")
    }
]));
```

Three things about it are deliberate, and each came from a measurement rather than a preference.

**A step counts when the site reacted, not when the move was made.** Assigning a field's value sets it and
raises none of the events a suggestion list hangs off, so an agent that reports its own movements cannot be
told apart from a broken one. Every step may name the `Effect` it is held to.

**How an element is addressed is stated, never inferred.** `Spot.Css` matches a selector; `Spot.Labelled`
finds the field a visible caption names. There is no implicit conversion from a string, because which of the
two is in use is exactly the fact worth being explicit about.

**A fruitless step explains itself.** The failure names the step and then describes the page it failed on: how
many elements the address matched, how many were visible, what fields were offered instead, and what the page
asked the site for while the step ran. A page that asked for nothing never noticed the move.

## Building

```bash
dotnet build
dotnet test --filter "Category!=Integration"   # no Docker needed
dotnet test                                    # adds the integration suites
```
