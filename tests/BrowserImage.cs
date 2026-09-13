using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Scraping.Browsers;

namespace Snail.Toolkit.Web.Tests.Browsers;

/// <summary>
/// Which browser the integration fixtures start, and which engine the toolkit has to be told about.
/// </summary>
/// <remarks>
/// Stock Playwright by default: that image is public, multi-architecture and always matches the referenced
/// client, so the suite runs on any machine with Docker. <c>SNAIL_BROWSER_IMAGE</c> points the same fixtures at
/// the patched build instead — the one that reports <c>navigator.webdriver === false</c> natively — and
/// <c>SNAIL_BROWSER_ENGINE</c> says which engine that image serves.
/// <para>
/// Both or neither. A Chromium client cannot connect to a Firefox server, so an image named without its engine
/// connects to nothing, and the failure arrives as a timeout rather than as an explanation.
/// </para>
/// <para>
/// The patched image is built by hand and published nowhere, so it is not the default: a suite that depends on
/// a local artefact fails on every machine that has not built it.
/// </para>
/// </remarks>
internal static partial class BrowserImage
{
    /// <summary>The Playwright version this project references, used for both the image and the server.</summary>
    private static string Version =>
        typeof(Microsoft.Playwright.Playwright).Assembly.GetName().Version!.ToString(3);

    /// <summary>The variable that names the image a fixture starts.</summary>
    public const string ImageVariable = "SNAIL_BROWSER_IMAGE";

    /// <summary>The variable that names the engine that image serves.</summary>
    public const string EngineVariable = "SNAIL_BROWSER_ENGINE";

    /// <summary>The image a fixture starts.</summary>
    public static string Name =>
        Environment.GetEnvironmentVariable(ImageVariable) is { Length: > 0 } named
            ? Matching(named)
            : $"mcr.microsoft.com/playwright:v{Version}";

    /// <summary>The engine the started image serves.</summary>
    public static Engine Engine =>
        Environment.GetEnvironmentVariable(EngineVariable) is { Length: > 0 } named
        && Enum.TryParse<Engine>(named, ignoreCase: true, out var engine)
            ? engine
            : Engine.Chromium;

    /// <summary>Refuses an image whose tag names a Playwright this project does not reference.</summary>
    /// <remarks>
    /// A server answers a client of another version with <c>428 Precondition Required</c> and nothing else, and
    /// through a fixture that arrives as a five-minute wait for a line that never comes. The patched image
    /// carries its client version in the tag precisely so the disagreement can be caught here, before anything
    /// starts. An image whose tag says nothing is taken at its word.
    /// </remarks>
    private static string Matching(string image)
    {
        var named = PlaywrightInTag().Match(image);

        if (named.Success && !Version.StartsWith(named.Groups["version"].Value, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{ImageVariable} names '{image}', which serves Playwright {named.Groups["version"].Value}, "
                + $"while this project references {Version}. They have to match.");

        return image;
    }

    [GeneratedRegex(@"pw(?<version>\d+\.\d+)")]
    private static partial Regex PlaywrightInTag();

    private static bool IsStock =>
        Name.StartsWith("mcr.microsoft.com/playwright", StringComparison.Ordinal);

    /// <summary>A user agent the served engine could plausibly have sent.</summary>
    private static string UserAgent => Engine switch
    {
        Engine.Firefox => "Mozilla/5.0 (X11; Linux x86_64; rv:155.0) Gecko/20100101 Firefox/155.0",
        Engine.WebKit => "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
        _ => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
    };

    extension(IServiceCollection services)
    {
        /// <summary>Gives the identity a user agent the served engine could have sent.</summary>
        /// <remarks>
        /// The built-in fingerprints are Chrome, and registration refuses them on any other engine: a Gecko
        /// browser carrying a Chrome user agent is a contradiction, and it was measured to cost the results
        /// it was supposed to fetch. Chromium keeps the built-in set, so the suite goes on observing what it
        /// always observed unless the fixtures are pointed at another engine.
        /// </remarks>
        public IServiceCollection MatchingFingerprint()
        {
            if (Engine is Engine.Chromium)
                return services;

            return services.Configure<IdentityOptions>(
                options => options.Fingerprints = [new Fingerprint { UserAgent = UserAgent }]);
        }
    }

    extension(ContainerBuilder builder)
    {
        /// <summary>Adds the command that starts a server and the line that says it is listening.</summary>
        /// <remarks>
        /// The stock image is a toolbox and has to be told what to run; the patched one is a browser server and
        /// starts one itself. They also announce readiness in different words, and waiting for the wrong line
        /// is a five-minute timeout rather than a failure anyone can read.
        /// </remarks>
        public ContainerBuilder Serving(int port)
            => IsStock
                ? builder
                    .WithCommand("/bin/sh", "-c", $"npx -y playwright@{Version} run-server --port {port} --host 0.0.0.0")
                    .WithWaitStrategy(Listening("Listening on ws://"))
                : builder.WithWaitStrategy(Listening("browser server ready"));
    }

    private static IWaitForContainerOS Listening(string line)
        => Wait.ForUnixContainer()
            .UntilMessageIsLogged(line, strategy => strategy.WithTimeout(TimeSpan.FromMinutes(5)));
}
