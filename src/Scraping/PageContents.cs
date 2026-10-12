using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Text;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// Turns a finished document into the content it carries, or the reason it carries none.
/// </summary>
/// <remarks>
/// This tail was written twice — once at the end of a plain read and once at the end of a walkthrough — and the
/// two copies drifted, the way two copies do. What drifted was the title: a page read by address carried one
/// and the same page walked to through a form did not, so one property of one type had two answers depending on
/// the road the page arrived by. Extracting the tail is not tidiness; it is the only thing that stops that
/// class of defect from happening again.
/// <para>
/// What is deliberately NOT here is the refusal and the HTTP status. A challenge on arrival and a challenge
/// after a form was submitted are different facts worded differently, and the status a walkthrough carries
/// belongs to the page it started on rather than to the one it ended on. Folding those in would have made one
/// of the two callers say something untrue.
/// </para>
/// </remarks>
public sealed class PageContents(ITextExtraction text, IOptions<ScrapingOptions> options)
{
    private readonly ContentBudget _budget = options.Value.MaxContentCharacters > 0
        ? ContentBudget.Of(options.Value.MaxContentCharacters)
        : ContentBudget.Unlimited;

    /// <summary>Gets what a read answers when nobody asks for anything else.</summary>
    internal Reading Reading { get; private init; } = Reading.Text;

    /// <summary>The same contents, answering <paramref name="reading"/> by default; how the engines' readers get theirs.</summary>
    internal PageContents Answering(Reading reading) => new(text, options) { Reading = reading };

    /// <summary>The content the document carries, or the reason it carries none.</summary>
    /// <remarks>
    /// Emptiness is judged on the rendered text rather than on the markup: a browser answers
    /// <c>&lt;html&gt;&lt;head&gt;&lt;/head&gt;&lt;body&gt;&lt;/body&gt;&lt;/html&gt;</c> for a blank page, so
    /// testing the markup for whitespace never finds one.
    /// <para>
    /// The budget lands on the extracted text and the markup goes back whole. Cutting markup by character ends
    /// the document mid-tag, and the callers that read markup — every browser-backed search engine — then lose
    /// every link past the cut while the page still looks like it parsed.
    /// </para>
    /// <para>
    /// SO NOTHING HERE BOUNDS THE MARKUP, and that is why the one bound it does have is a refusal rather than
    /// a cut. Parsing is quadratic in nesting depth, so markup nested a thousand tags deep costs seconds of
    /// CPU per page — and this is the only place that can say so in the caller's own vocabulary. Left to
    /// itself the refusal would have arrived as an empty text and been reported as
    /// <see cref="FailureKind.Empty"/>: a page that published a great deal, described as having published
    /// nothing.
    /// </para>
    /// </remarks>
    public PageOutcome Judged(Document document) => Judged(document, Reading);

    /// <summary>The content the document carries, read as <paramref name="reading"/> asks, or the reason it carries none.</summary>
    internal PageOutcome Judged(Document document, Reading reading)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.Rendered))
            return PageOutcome.Failed(new PageFailure(FailureKind.Empty, "The page rendered no content.")
            {
                Address = document.Address,
                StatusCode = document.StatusCode,
                UserAgent = document.Identity.UserAgent
            });

        try
        {
            var (clean, title, parsed) = Read(document.Html, reading);
            var budgeted = _budget.Apply(clean);

            return PageOutcome.Success(
                new PageContent(document.Address, document.Html, budgeted.Text, DateTimeOffset.UtcNow)
                {
                    Final = Link.Of(document.FinalUrl),
                    Title = title,
                    StatusCode = document.StatusCode,
                    UserAgent = document.Identity.UserAgent,
                    IsTruncated = budgeted.IsTruncated,
                    OriginalLength = budgeted.OriginalLength,
                    Parsed = reading is Reading.Text ? null : parsed
                });
        }
        catch (MarkupTooDeepException refused)
        {
            return PageOutcome.Failed(new PageFailure(FailureKind.Unreadable, refused.Message)
            {
                Address = document.Address,
                StatusCode = document.StatusCode,
                UserAgent = document.Identity.UserAgent,
                Cause = refused
            });
        }
    }

    /// <summary>Reads the page's text and title, from one parse of its markup where the extraction is this library's own.</summary>
    /// <remarks>
    /// Each of the two parsed the markup on its own, and the parse is most of what either costs. Another
    /// extraction is asked as it always was: what it does with markup, a ceiling on nesting included, is its own.
    /// <para>
    /// A search engine's results page is parsed and nothing more. Its text and title were extracted for every
    /// query and read by nobody, since an engine reads its results out of the markup: measured on 2026-10-12 by
    /// the audit, three parses and the text cost 4.34 MB a page, one parse alone 1.23 MB.
    /// </para>
    /// </remarks>
    private (string Text, string? Title, HtmlDocument? Parsed) Read(string html, Reading reading)
    {
        if (text is not TextExtraction own || string.IsNullOrWhiteSpace(html))
            return (text.Clean(html), text.Title(html), null);

        var parsed = Markup.Read(html);

        return reading is Reading.DocumentOnly
            ? (string.Empty, null, parsed)
            : (own.Clean(parsed), own.Title(parsed), parsed);
    }
}
