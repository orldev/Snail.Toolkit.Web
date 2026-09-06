using System.Diagnostics.CodeAnalysis;

namespace Snail.Toolkit.Web;

/// <summary>
/// What an operation produced, or why it produced nothing.
/// </summary>
/// <remarks>
/// Both engines used to answer with a bare <see cref="string"/>, so a captcha wall, a navigation timeout
/// and a genuinely empty document all arrived as an empty result and no caller could tell a site that has
/// nothing to say from one that refused to talk. A successful outcome always carries a value:
/// <see cref="Success"/> refuses <see langword="null"/>, so success and "there is something to read" are
/// the same fact.
/// </remarks>
public sealed record Outcome<TValue, TFailure>
    where TValue : class
    where TFailure : class
{
    private readonly TValue? _value;

    private Outcome(TValue? value, TFailure? failure)
    {
        _value = value;
        Failure = failure;
    }

    /// <summary>Gets the failure, or <see langword="null"/> when the operation succeeded.</summary>
    public TFailure? Failure { get; }

    /// <summary>Gets whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool IsSuccess => Failure is null;

    /// <summary>Gets the value a successful operation produced.</summary>
    /// <exception cref="InvalidOperationException">The operation failed.</exception>
    /// <remarks>
    /// Reading this on a failed outcome is a mistake in the calling code rather than a runtime condition,
    /// so it throws instead of handing back an empty value. Use <see cref="TryGetValue"/> where the outcome
    /// is open.
    /// </remarks>
    public TValue Value => _value ?? throw new InvalidOperationException(
        $"The operation failed and carries no value. {Failure}");

    /// <summary>Reads the value of a successful outcome.</summary>
    /// <remarks>
    /// A false answer states that <see cref="Failure"/> is there, so the failing branch reads it without the
    /// null check the compiler would otherwise demand.
    /// </remarks>
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool TryGetValue([NotNullWhen(true)] out TValue? value)
    {
        value = _value;

        return _value is not null;
    }

    /// <summary>Reports a successful operation.</summary>
    public static Outcome<TValue, TFailure> Success(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new Outcome<TValue, TFailure>(value, null);
    }

    /// <summary>Reports a failed operation.</summary>
    public static Outcome<TValue, TFailure> Failed(TFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new Outcome<TValue, TFailure>(null, failure);
    }
}
