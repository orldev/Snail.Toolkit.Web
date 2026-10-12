using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Web.Identity;
using Snail.Toolkit.Web.Robots;

namespace Snail.Toolkit.Web.Benchmarks;

/// <summary>What every attempt pays before it reaches a site: an identity, and the site's rules asked twice.</summary>
/// <remarks>
/// The attempts ask for an identity, whether the address is allowed and how long the site asks to be left alone,
/// on every try of every page. The rules are already read, as they are for every page of a site but the first.
/// </remarks>
[MemoryDiagnoser]
public class Bookkeeping
{
    private readonly Uri _address = new("https://www.example.com/catalogue/item-42?ref=list");

    private IdentityRotation _identities = null!;

    private SiteRules _rules = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var robots = RobotsFile.Parse("User-agent: *\nDisallow: /admin\nDisallow: /*.pdf$\nAllow: /admin/public\nCrawl-delay: 2\n");

        _identities = new IdentityRotation(Options.Create(new IdentityOptions()), new NoProxies(), NullLogger<IdentityRotation>.Instance);
        _rules = new SiteRules(new Canned(robots), Options.Create(new RobotsOptions { IsObeyed = true }));

        var allowed = await _rules.IsAllowedAsync(_address, null);
        var delay = await _rules.DelayAsync(_address, null);

        if (!allowed || delay != TimeSpan.FromSeconds(2))
            throw new InvalidOperationException("The rules did not answer the delay and the permission the file sets.");
    }

    [Benchmark]
    public Task<int> Request() => RequestAsync();

    [Benchmark]
    public WebIdentity Identity() => _identities.Next();

    private async Task<int> RequestAsync()
    {
        var identity = _identities.Next();
        var allowed = await _rules.IsAllowedAsync(_address, identity.Proxy);
        var delay = await _rules.DelayAsync(_address, identity.Proxy);

        return (allowed ? 1 : 0) + (delay?.Seconds ?? 0) + identity.UserAgent.Length;
    }

    private sealed class NoProxies : IProxyRotation
    {
        public ProxyEndpoint? For(string host) => null;

        public ProxyEndpoint? Next() => null;
    }

    private sealed class Canned(RobotsFile file) : IRobotsSource
    {
        public Task<RobotsFile> ReadAsync(Uri origin, ProxyEndpoint? proxy, CancellationToken ct = default)
            => Task.FromResult(file);
    }
}
