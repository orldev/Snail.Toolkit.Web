namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// A credential the page issues to itself before its own service will answer.
/// </summary>
/// <remarks>
/// This describes the invisible reCAPTCHA v3 pattern, which is what a registry measured on 2026-09-11 uses:
/// the page calls <c>grecaptcha.execute(key, {action})</c>, receives a token and appends it to the request.
/// No human is asked anything — the token exists only because a browser ran the site's own script, which is
/// why such a service cannot be reached over plain HTTP and is not the same thing as a picture code meant to
/// stop automation outright.
/// <para>
/// <see cref="Action"/> is not decoration: that registry validates it against the route being called, issuing
/// <c>search_fedresurs</c> for one endpoint and <c>search_notary</c> for the other, and a token minted under
/// the wrong action comes back as "captcha not passed" even though it minted perfectly well.
/// </para>
/// </remarks>
public sealed record Mint(string Key, string Action)
{
    /// <summary>The query parameter the token is appended as.</summary>
    public string Parameter { get; init; } = "token";

    /// <summary>How long the page is given to issue the token.</summary>
    public TimeSpan Within { get; init; } = TimeSpan.FromSeconds(15);
}
