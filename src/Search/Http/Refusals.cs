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
    private static bool IsRefusal(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.Forbidden
            or HttpStatusCode.ServiceUnavailable;
}
