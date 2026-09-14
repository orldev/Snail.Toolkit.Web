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

            _rests[provider] = new Rest(refusals, Until(clock.GetUtcNow(), length));

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
    /// <remarks>
    /// The doubling saturates rather than overflowing, and the difference is only visible on a ceiling the
    /// doubling cannot reach. <see cref="SearchOptions.MaxRefusalCooldown"/> is validated as no shorter than
    /// the first cooldown and nothing more, so <see cref="TimeSpan.MaxValue"/> — which is how somebody writes
    /// "keep backing off, never give up" — passes start-up and then sits above every value a
    /// <see cref="TimeSpan"/> can hold. The loop's own condition stayed true right up to the multiplication
    /// that could not be performed: five minutes doubled past forty-one times threw
    /// <see cref="OverflowException"/> out of <see cref="Refused"/>, which is called while recording a
    /// refusal — so the engine that refused took the search down instead of being rested, and the exception
    /// named neither.
    /// </remarks>
    private TimeSpan LengthOf(int refusals)
    {
        var ceiling = _options.MaxRefusalCooldown;
        var length = _options.RefusalCooldown;

        for (var doubling = 1; doubling < refusals && length < ceiling; doubling++)
        {
            if (length.Ticks > ceiling.Ticks / 2)
                return ceiling;

            length *= 2;
        }

        return length < ceiling ? length : ceiling;
    }

    /// <summary>When a rest of this length ends, or the end of the calendar when it outlasts it.</summary>
    /// <remarks>
    /// The second place the same misconfiguration lands, and it is worth naming separately because fixing the
    /// first uncovered it. Saturating the doubling means a ceiling of <see cref="TimeSpan.MaxValue"/> is
    /// answered with <see cref="TimeSpan.MaxValue"/> — and adding that to the current instant is itself past
    /// what a <see cref="DateTimeOffset"/> holds, so the overflow simply moved one line down. A rest longer
    /// than there is calendar left is a rest until the end of it, which is what the caller asked for.
    /// </remarks>
    private static DateTimeOffset Until(DateTimeOffset from, TimeSpan length)
        => length < DateTimeOffset.MaxValue - from ? from + length : DateTimeOffset.MaxValue;

    private readonly record struct Rest(int Refusals, DateTimeOffset Until);
}
