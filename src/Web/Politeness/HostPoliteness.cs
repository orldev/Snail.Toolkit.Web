using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Politeness;

/// <summary>
/// Holds a per-host gate: a concurrency limit and a minimum gap between requests.
/// </summary>
/// <remarks>
/// The delay is measured from the moment the previous request to that host finished, so a slow page does not
/// accumulate a debt of queued requests that all fire the instant it completes. The gap is the configured
/// default for most hosts and a longer one for those named in <see cref="PolitenessOptions.HostDelays"/>,
/// decided once, when the host's gate is created.
/// </remarks>
public sealed class HostPoliteness(IOptions<PolitenessOptions> options) : IPoliteness, IDisposable
{
    private readonly PolitenessOptions _options = options.Value;
    private readonly Dictionary<string, TimeSpan> _gentler = new(options.Value.HostDelays, StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Gate> _gates = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IAsyncDisposable> EnterAsync(
        Uri url,
        TimeSpan? requested = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var gate = _gates.GetOrAdd(
            url.Host,
            host => new Gate(Math.Max(1, _options.MaxConcurrentPerHost), FloorFor(host)));

        await gate.Slots.WaitAsync(ct);

        try
        {
            await DelayAsync(gate, requested, ct);

            return new Turn(gate);
        }
        catch
        {
            gate.Slots.Release();
            throw;
        }
    }

    /// <inheritdoc />
    public void Rest(Uri url, TimeSpan gap)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (gap <= TimeSpan.Zero)
            return;

        var gate = _gates.GetOrAdd(
            url.Host,
            host => new Gate(Math.Max(1, _options.MaxConcurrentPerHost), FloorFor(host)));

        var until = DateTimeOffset.UtcNow + gap;

        if (until > gate.RestingUntil)
            gate.RestingUntil = until;
    }

    private static async Task DelayAsync(Gate gate, TimeSpan? requested, CancellationToken ct)
    {
        var wanted = requested > gate.Floor ? requested.Value : gate.Floor;
        var ready = gate.ReleasedAt + wanted;
        var opens = ready > gate.RestingUntil ? ready : gate.RestingUntil;
        var remaining = opens - DateTimeOffset.UtcNow;

        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct);
    }

    /// <summary>The smallest gap this host gets: its own, its nearest named parent's, or the default.</summary>
    /// <remarks>
    /// The host is tried whole first and then shortened one label at a time, so <c>news.example.com</c> looks
    /// for <c>news.example.com</c>, then <c>example.com</c>, then <c>com</c>. A name that merely ends the same
    /// way is not a parent: the cut is always at a dot.
    /// </remarks>
    private TimeSpan FloorFor(string host)
    {
        var candidate = host;

        while (true)
        {
            if (_gentler.TryGetValue(candidate, out var gap))
                return gap;

            var dot = candidate.IndexOf('.');

            if (dot < 0)
                return _options.MinDelayPerHost;

            candidate = candidate[(dot + 1)..];
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var gate in _gates.Values)
            gate.Slots.Dispose();

        _gates.Clear();
    }

    private sealed class Gate(int concurrency, TimeSpan floor)
    {
        public SemaphoreSlim Slots { get; } = new(concurrency, concurrency);

        public TimeSpan Floor { get; } = floor;

        public DateTimeOffset ReleasedAt { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset RestingUntil { get; set; } = DateTimeOffset.MinValue;
    }

    private sealed class Turn(Gate gate) : IAsyncDisposable
    {
        private int _isReleased;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isReleased, 1) == 0)
            {
                gate.ReleasedAt = DateTimeOffset.UtcNow;
                gate.Slots.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
