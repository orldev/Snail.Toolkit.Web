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
    private long _sweepDue = DateTimeOffset.MinValue.UtcTicks;

    /// <summary>How many hosts are currently remembered.</summary>
    internal int RememberedHosts => _gates.Count;

    /// <inheritdoc />
    public async Task<IAsyncDisposable> EnterAsync(
        Uri url,
        TimeSpan? requested = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var gate = GateFor(url.Host);

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

        GateFor(url.Host).RestUntil(DateTimeOffset.UtcNow + gap);
    }

    /// <summary>This host's gate, forgetting the hosts nobody has spoken to in a while first.</summary>
    /// <remarks>
    /// A gate per host and nothing that ever removed one: a crawl of many sites grew this map for as long as it
    /// ran, and the hosts of the web are not a finite set. What bounds it now is time rather than a count — the
    /// map holds the hosts touched within <see cref="PolitenessOptions.ForgetHostAfter"/>, which is the set
    /// that pacing decisions can still depend on. A count would have had to be guessed; this is derived from
    /// what the gate is for.
    /// <para>
    /// Sweeping happens on the arrival of an unknown host, and at most once a quarter of that window, because
    /// a sweep reads the whole map and doing that per new host would be quadratic over a crawl.
    /// </para>
    /// </remarks>
    private Gate GateFor(string host)
    {
        if (_gates.TryGetValue(host, out var known))
            return known;

        ForgetIdleHosts();

        return _gates.GetOrAdd(host, name => new Gate(Math.Max(1, _options.MaxConcurrentPerHost), FloorFor(name)));
    }

    /// <summary>Drops the gates that no longer decide anything.</summary>
    /// <remarks>
    /// A gate is dropped only when nobody is inside it, it is not holding the host back, and its last turn
    /// ended longer ago than the window — so a rest a site asked for cannot be forgotten by a sweep, and
    /// neither can a host with a request in flight. An idle gate carries no decision: recreating it gives the
    /// same answer as keeping it.
    /// </remarks>
    private void ForgetIdleHosts()
    {
        var now = DateTimeOffset.UtcNow;
        var due = Interlocked.Read(ref _sweepDue);

        if (now.UtcTicks < due)
            return;

        if (Interlocked.CompareExchange(ref _sweepDue, (now + _options.ForgetHostAfter / 4).UtcTicks, due) != due)
            return;

        var idleSince = now - _options.ForgetHostAfter;

        foreach (var (host, gate) in _gates)
            if (gate.IsIdleSince(idleSince))
                _gates.TryRemove(new KeyValuePair<string, Gate>(host, gate));
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
    /// <remarks>
    /// The semaphores are deliberately left alone. A turn in flight releases its own when the page it paces
    /// finishes, and that is after shutdown by definition — a crawl is cancelled while pages are still open.
    /// Disposing them underneath answered the caller's <c>await using</c> with an <c>ObjectDisposedException</c>
    /// naming a semaphore, which is the last thing anyone reads while looking for why the run stopped. Nothing
    /// here holds a handle worth reclaiming: <see cref="SemaphoreSlim"/> allocates one only for
    /// <c>AvailableWaitHandle</c>, which nobody asks for. What is left to do at shutdown is forget the hosts.
    /// </remarks>
    public void Dispose() => _gates.Clear();

    /// <summary>One host's turn-taking: how many at once, how long between, and until when it is left alone.</summary>
    /// <remarks>
    /// Both instants are held as ticks and moved only forwards, by a compare-and-exchange that keeps the later
    /// of the two. They used to be <see cref="DateTimeOffset"/> properties written under no lock at all, which
    /// cost two things at once. A rest was lost outright: <c>Rest</c> compared and then assigned, so a caller
    /// carrying a short gap could read the field before a longer rest was written and store its own afterwards
    /// — measured on 2026-09-14 by sixty-four simultaneous callers, five to eight rounds in five hundred ended
    /// with the host open after a site had asked for thirty seconds. And a reader could in principle see half
    /// of one write beside half of another, since twelve bytes are not written at once, which invents a rest
    /// rather than losing one.
    /// </remarks>
    private sealed class Gate(int concurrency, TimeSpan floor)
    {
        private long _releasedAt = DateTimeOffset.MinValue.UtcTicks;

        private long _restingUntil = DateTimeOffset.MinValue.UtcTicks;

        public SemaphoreSlim Slots { get; } = new(concurrency, concurrency);

        public TimeSpan Floor { get; } = floor;

        public bool IsIdleSince(DateTimeOffset before)
            => Slots.CurrentCount == concurrency && ReleasedAt <= before && RestingUntil <= before;

        public DateTimeOffset ReleasedAt => Instant(ref _releasedAt);

        public DateTimeOffset RestingUntil => Instant(ref _restingUntil);

        public void Released(DateTimeOffset at) => MoveForwardTo(ref _releasedAt, at);

        public void RestUntil(DateTimeOffset until) => MoveForwardTo(ref _restingUntil, until);

        private static DateTimeOffset Instant(ref long held) => new(Interlocked.Read(ref held), TimeSpan.Zero);

        private static void MoveForwardTo(ref long held, DateTimeOffset wanted)
        {
            var moved = wanted.UtcTicks;
            var standing = Interlocked.Read(ref held);

            while (moved > standing)
            {
                var found = Interlocked.CompareExchange(ref held, moved, standing);

                if (found == standing)
                    return;

                standing = found;
            }
        }
    }

    private sealed class Turn(Gate gate) : IAsyncDisposable
    {
        private int _isReleased;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isReleased, 1) == 0)
            {
                gate.Released(DateTimeOffset.UtcNow);
                gate.Slots.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
