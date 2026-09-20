# Verified by removal

Failure is a value. A comment carries a measurement. The reason something was refused lives in the code, at
the spot where it will be proposed again.

These are not aspirations. Every one of them leaves a countable trace in `src`, and the page ends with the
commands that count them — because the second principle applies to this page too.

## Failure is a value, not an empty answer

A fact about the world — the site refused, a captcha stood in the way, `robots.txt` forbade the URL, the
markup was too deep to read — comes back as `Outcome<TValue, TFailure>`. A mistake in the calling code throws
at once. The two are never mixed, and the ratio shows it.

| Occurrence | Count | What it means |
| --- | --- | --- |
| `throw new` | 6 | the whole of the exception surface in six packages |
| `ArgumentNullException.ThrowIfNull` | 73 | a caller's mistake, refused on the doorstep |
| `Outcome.Failed` | 22 | a fact about the site, handed back as data |

Cancellation is the third case and it is never translated: a cancelled run stops, it does not come back as ten
thousand cancelled items.

The origin is recorded in `Outcome.cs`: both engines used to answer with a bare string, so a captcha, a
navigation timeout and a genuinely empty document all arrived looking identical.

## A comment carries a measurement, not a paraphrase

Not "this method parses HTML", but "measured 2026-09-14 against HtmlAgilityPack 1.13.0, on the 1 MB stack the
thread pool gives its workers: 6,000 levels return, 8,000 raise `StackOverflowException`".

There are **73 dated measurements** in the remarks of `src`. The consequence is the rule itself: a comment
here has to be falsifiable. If it carries a number, the number can be re-measured and the comment proved
wrong.

## Name the alternative that was refused

The code rarely says what it does. It says what it does *not* do, and why.

| Phrase | Count | What it is doing |
| --- | --- | --- |
| `rather than` | 172 | naming the road not taken |
| `is not` | 131 | defining by exclusion |
| `used to` | 32 | what stood here before, and what it cost |

A file reads as a record of decisions rather than as a snapshot of the current state.

## A refusal is recorded where it will be proposed again

Not in the plan, not in the commit message, not in a ticket. A plan gets closed and forgotten; the idea comes
back — from the next audit, from another engineer, from the same one a month later. So the argument against it
waits at the spot where it will be reached for.

| The idea | Where the argument against it lives |
| --- | --- |
| Add `IOptionsMonitor`, settings look reloadable | `BindOnce` |
| Version the leaf packages separately | `Directory.Build.props` |
| Add a `FailureKind.Cancelled` | the enum itself |
| Extract opening the browser out of the pool | `BrowserPool` remarks |
| Make the nesting ceiling a setting | `Markup` remarks |
| Publish `Fanout` and `Spread` | `Web.csproj` |
| Guard `robots.txt` parsing by allocation | the timing check's remarks |

Three of four items in one audit round were disproved by paragraphs that already stood in the repository. The
audit had been written without reading them.

## A cure is verified by removing it

Every fix has to turn a check red when it is taken out — and taken out **one at a time**. "The batch of
changes made the test green" does not establish that each one was needed.

It has caught real things. A confirm-and-retry loop in the politeness gate was written and then **removed**,
because no check could tell it apart from its absence. A check on the pool's idle clock turned out to stay
green with the line it guarded deleted — it was watching nothing. And an allocation guard proposed for
`robots.txt` parsing was written and thrown away when the measurement said 8.94 MB with the optimisation and
8.99 MB without it: the cure saves CPU, not memory, and a check that is green either way is worse than no
check, because it looks like a guard.

A retry loop on the path every page takes, justified by no test that can distinguish it, is a worse trade than
not having it.

## A number comes from a measurement, and a threshold from two

The degree of parallelism, the 450 ms scheduling threshold, the nesting ceiling of 1,000, the five-second
grace period — each has a measurement before and a measurement after, recorded beside it.

A threshold derived on paper tests the arithmetic, not the code: one of them once passed the *unfixed*
implementation with seven milliseconds to spare, and nothing would have revealed that, because after the fix
it was green exactly as expected.

Where the cost is memory the threshold is in bytes rather than in milliseconds, because a timing threshold
measures the machine as much as the code. Bounded read-ahead over a single host costs 13 KB and, with the
bound removed, 20.8 GB; refusing markup deeper than the ceiling costs 682 KB at five thousand levels and
686 KB at twenty thousand, against 5.3 MB and 21.3 MB parsed. That the two refusal figures are equal is a
statement timing cannot make: cost that stops growing with the input is the whole of what a ceiling is for.

## A type, where a paragraph would have to be obeyed

If a rule can be made inexpressible, it is made inexpressible instead of written down.

`Permits` has no `Dispose` — because the paragraph saying "do not dispose this semaphore" was honoured for one
of the two it applied to, and the one it missed cost a caller that hung forever. `Fanout` takes its failure
callback as a **required parameter** — because it used to be a promise in the remarks, and a test in the
neighbouring file asserted the opposite.

## Honesty about what is not covered

"Not covered, honestly" appears in the code in those words, against the branch that cannot be reached without a
locally launched browser.

The rule is stated in `LiveFactAttribute`: a suite that reports a check as skipped is admitting it did not
run, while one that returns early and passes is **claiming a check it never made**.

## Packages split by cost, not by layer

Six packages, divided not into controllers and services and repositories but by **what the consumer pays
for**. `Search` does not depend on `Scraping`, so somebody who only ever calls an HTTP provider never carries a
browser driver. Every split is explained in the `.csproj` that makes it.

## Deliberately absent

| Not here | Instead |
| --- | --- |
| SOLID ceremony | 28 interfaces, each an extension point with a consumer; inheritance occurs exactly once |
| A mocking library | fakes written by hand; a seventy-member browser context faked through `DispatchProxy` |
| A third-party result type | `Outcome`, seventy lines, written here |
| `Manager`, `Helper`, `Util`, `Impl`, `Base` | not one occurrence — `Gate`, `Turn`, `Errand`, `Trail`, `Setbacks` |
| BenchmarkDotNet | allocation is deterministic; three lines of plain xunit answer it in milliseconds, and throughput is measured in `probes/` |

## What these principles cost

**An entry price.** Comments are about a third of the source and the vocabulary is the project's own. A
newcomer's first day goes on "why is it like this", not "what does this method do". That is the right trade,
but it is a trade.

**An expiry risk.** Seventy-three dated measurements are seventy-three claims that can drift away from reality
after an update to HtmlAgilityPack or Playwright. Nothing turns red when a measurement goes stale — this is the
one principle here held up by discipline rather than by code.

## Re-measure this page

Every number above came from these commands. A principle that cannot be counted does not belong on this page.

```bash
# the exception ratio — 6 / 73 / 22
S=$(find src -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*")
echo "$S" | xargs grep -c "throw new"   | awk -F: '{s+=$2} END {print s}'
echo "$S" | xargs grep -c "ThrowIfNull" | awk -F: '{s+=$2} END {print s}'
echo "$S" | xargs grep -c "\.Failed("   | awk -F: '{s+=$2} END {print s}'

# the shape of the prose — 172 / 131 / 32
for p in "rather than" "is not" "used to"; do
  printf "%-14s %s\n" "$p" "$(echo "$S" | xargs grep -o "$p" | wc -l)"
done

# dated measurements — 73
echo "$S" | xargs grep -oE "20[0-9]{2}-[0-9]{2}-[0-9]{2}" | wc -l

# the words that never appear — 0
echo "$S" | xargs grep -cE "(Manager|Helper|Util|Impl|Base)\b" | grep -v ":0"
```
