using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Search.Http;

/// <summary>
/// Presents a coherent identity on every outgoing request.
/// </summary>
/// <remarks>
/// The headers come from the same rotation the browser side uses, so a user agent claiming Chrome on Windows
/// is never sent beside client hints claiming macOS. Only headers a real client sends on every request are set;
/// <c>Accept</c> and <c>Accept-Encoding</c> are left to <c>HttpClient</c>, which varies them the way a browser
/// does.
/// The identity's proxy is deliberately not applied. A pooled handler cannot switch proxy per request, and
/// pretending otherwise would route traffic somewhere other than the configuration says — a consumer that needs
/// proxying configures it on the primary handler of the named client.
/// </remarks>
public sealed class IdentityHandler(IIdentityRotation identities) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identity = identities.Next();

        Offer(request, "User-Agent", identity.UserAgent);
        Offer(request, "Accept-Language", identity.AcceptLanguage);

        if (identity.SupportsClientHints)
        {
            Offer(request, "Sec-Ch-Ua",
                $"\"Not-A.Brand\";v=\"99\", \"{identity.Brand}\";v=\"{identity.MajorVersion}\", \"Chromium\";v=\"{identity.MajorVersion}\"");
            Offer(request, "Sec-Ch-Ua-Mobile", "?0");
            Offer(request, "Sec-Ch-Ua-Platform", $"\"{identity.Platform}\"");
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static void Offer(HttpRequestMessage request, string name, string value)
    {
        if (!request.Headers.Contains(name))
            request.Headers.TryAddWithoutValidation(name, value);
    }
}
