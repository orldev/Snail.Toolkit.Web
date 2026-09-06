using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Politeness;

/// <summary>
/// Holds a per-host gate: a concurrency limit and a minimum gap between requests.
/// </summary>
/// <remarks>
/// The delay is measured from the moment the previous request to that host finished, so a slow page does not
/// accumulate a debt of queued requests that all fire the instant it completes.
/// </remarks>
public sealed class HostPoliteness(IOptions<PolitenessOptions> options) : IPoliteness, IDisposable
{
    private readonly PolitenessOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Gate> _gates = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IAsyncDisposable> EnterAsync(
        Uri url,
        TimeSpan? requested = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var gate = _gates.GetOrAdd(url.Host, _ => new Gate(Math.Max(1, _options.MaxConcurrentPerHost)));

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

    private async Task DelayAsync(Gate gate, TimeSpan? requested, CancellationToken ct)
    {
        var wanted = requested > _options.MinDelayPerHost ? requested.Value : _options.MinDelayPerHost;

        if (wanted <= TimeSpan.Zero)
            return;

        var since = DateTimeOffset.UtcNow - gate.ReleasedAt;
        var remaining = wanted - since;

        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var gate in _gates.Values)
            gate.Slots.Dispose();

        _gates.Clear();
    }

    private sealed class Gate(int concurrency)
    {
        public SemaphoreSlim Slots { get; } = new(concurrency, concurrency);

        public DateTimeOffset ReleasedAt { get; set; } = DateTimeOffset.MinValue;
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
