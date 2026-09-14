using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping;

/// <summary>
/// A page the browser has finished with, before anyone has decided what it is.
/// </summary>
/// <remarks>
/// Written as a record rather than as six parameters because three of them are strings and two of those are a
/// document and an address. A call that swapped them would compile, run, and answer with a page whose text was
/// its own URL.
/// </remarks>
public sealed record Document
{
    /// <summary>Gets the address the read was asked for.</summary>
    public required Link Address { get; init; }

    /// <summary>Gets the identity the page was shown to the site under.</summary>
    public required WebIdentity Identity { get; init; }

    /// <summary>Gets the markup as the browser has it after scripts have run.</summary>
    public required string Html { get; init; }

    /// <summary>Gets the text the page renders, which is what emptiness is judged on.</summary>
    public required string? Rendered { get; init; }

    /// <summary>Gets the address the browser ended on, which redirects make different from the one asked for.</summary>
    public required string FinalUrl { get; init; }

    /// <summary>Gets the status the navigation answered with, when there was a response.</summary>
    public int? StatusCode { get; init; }
}
