using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Web.Search;

/// <summary>
/// Keeps a refused provider out of the set for a rest that doubles with every further refusal.
/// </summary>
/// <remarks>
/// The first refusal starts a rest of <see cref="SearchOptions.RefusalCooldown"/>; a refusal after the rest has
/// run out doubles it, up to <see cref="SearchOptions.MaxRefusalCooldown"/>; an answer clears the record. The
/// run of refusals is remembered across rests on purpose: an engine that refuses, waits out five minutes and
/// refuses again is saying that five minutes was not enough. Only a refusal counts — a fault leaves the record
/// untouched, because resting an engine over a parser that no longer follows its markup would hide the one
/// thing that needed fixing.
/// <para>
/// There is no separate probing state. A rest that has run out lets the next request through, and the answer
/// to that request either clears the record or doubles the rest; that is the whole mechanism.
/// </para>
/// </remarks>
public sealed class SearchCooldown(IOptions<SearchOptions> options, TimeProvider clock) : ISearchCooldown
{
    private readonly SearchOptions _options = options.Value;
    private readonly Dictionary<string, Rest> _rests = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public bool IsResting(string provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_gate)
            return _rests.TryGetValue(provider, out var rest) && rest.Until > clock.GetUtcNow();
    }

    /// <inheritdoc />
    public TimeSpan Refused(string provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_gate)
        {
            var refusals = _rests.TryGetValue(provider, out var earlier) ? earlier.Refusals + 1 : 1;
            var length = LengthOf(refusals);

            _rests[provider] = new Rest(refusals, clock.GetUtcNow() + length);

            return length;
        }
    }

    /// <inheritdoc />
    public void Answered(string provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_gate)
            _rests.Remove(provider);
    }

    /// <summary>The rest a run of refusals has grown to: the configured one, doubled once per further refusal, never past the ceiling.</summary>
    private TimeSpan LengthOf(int refusals)
    {
        var length = _options.RefusalCooldown;

        for (var doubling = 1; doubling < refusals && length < _options.MaxRefusalCooldown; doubling++)
            length *= 2;

        return length < _options.MaxRefusalCooldown ? length : _options.MaxRefusalCooldown;
    }

    private readonly record struct Rest(int Refusals, DateTimeOffset Until);
}
