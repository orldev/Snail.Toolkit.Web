using System.Net;

namespace Snail.Toolkit.Web.Search.Http;

/// <summary>
/// Tells an engine asking to be left alone from an engine that is simply broken.
/// </summary>
/// <remarks>
/// The two call for opposite reactions, and the two engines that fetch over HTTP made no distinction at all:
/// both called <c>EnsureSuccessStatusCode</c>, so a 429 — the one status that says in words "come back later"
/// — arrived at the search as a fault, which leaves the record untouched. The engine was then asked again at
/// once, which is how a pause becomes a ban. Only the browser-backed engines were ever rested.
/// </remarks>
internal static class Refusals
{
    /// <summary>Reads a challenge page as the refusal it is, whatever status carried it.</summary>
    /// <remarks>
    /// The other half of this class, and the half that was missing until 2026-09-15. A status says nothing
    /// when the wall arrives as a successful one: measured that day, an engine answered <c>202</c> with a
    /// captcha page, the parser found no results in it, and the search recorded NO RESULTS — a statement
    /// about the QUERY — for a page that was a statement about the ADDRESS. The engine was then asked again
    /// at the next query, because only a refusal rests an engine, which is how a wall becomes a ban.
    /// <para>
    /// The signal is named in the message because the caller cannot see the page: "refused" alone sends a
    /// reader to look at the query, and <c>markup:confirm this search was made by a human</c> sends them to
    /// look at the address.
    /// </para>
    /// </remarks>
    public static SearchFailure Wall(string provider, string signal)
        => new(SearchFailureKind.Refused, $"{provider} answered a challenge page ({signal}).");

    /// <summary>Reads a status that is not success as the reason there are no results.</summary>
    public static SearchFailure Of(string provider, HttpStatusCode status)
        => IsRefusal(status)
            ? new SearchFailure(SearchFailureKind.Refused, $"{provider} answered {(int)status} {status}.")
            : new SearchFailure(SearchFailureKind.Faulted, $"{provider} answered {(int)status} {status}.");

    /// <summary>Answers whether the status is the engine turning the request away rather than failing.</summary>
    /// <remarks>
    /// A 429 asks for a later attempt in so many words. A 403 is the shape a consent wall and a blocked address
    /// both arrive in. A 503 is the server blaming itself, and an engine under load that is asked harder is an
    /// engine that stays under load. Everything else — a 404, a 500 — says nothing about the engine's patience.
    /// </remarks>
    public static bool IsRefusal(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.Forbidden
            or HttpStatusCode.ServiceUnavailable;
}
