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
    public PageOutcome Judged(Document document)
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
            var budgeted = _budget.Apply(text.Clean(document.Html));

            return PageOutcome.Success(
                new PageContent(document.Address, document.Html, budgeted.Text, DateTimeOffset.UtcNow)
                {
                    Final = Link.Of(document.FinalUrl),
                    Title = text.Title(document.Html),
                    StatusCode = document.StatusCode,
                    UserAgent = document.Identity.UserAgent,
                    IsTruncated = budgeted.IsTruncated,
                    OriginalLength = budgeted.OriginalLength
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
}
