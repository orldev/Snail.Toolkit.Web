namespace Snail.Toolkit.Web.Politeness;

/// <summary>
/// Paces requests so that no single host sees a burst.
/// </summary>
public interface IPoliteness
{
    /// <summary>Waits for this host's turn and holds it until the returned handle is disposed.</summary>
    /// <param name="url">The address about to be fetched; only its host matters.</param>
    /// <param name="requested">A gap the site itself asked for, when it asked for one.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <remarks>
    /// Limits are per host, so parallelism across a list of many sites is unaffected while one site never sees
    /// more than it allows. Request rate is the signal a site reads first, and the engines previously had no
    /// answer to it at all — every worker hit the same host as fast as the transport could load.
    /// <para>
    /// A gap the site asked for never shortens the configured one. Both are floors, so the wait is the longer
    /// of the two: a site asking for one second cannot talk a careful caller down to it.
    /// </para>
    /// </remarks>
    Task<IAsyncDisposable> EnterAsync(Uri url, TimeSpan? requested = null, CancellationToken ct = default);

    /// <summary>Holds a host back for a gap it has just asked for.</summary>
    /// <param name="url">The address that was refused; only its host matters.</param>
    /// <param name="gap">How long the site asked to be left alone.</param>
    /// <remarks>
    /// <see cref="EnterAsync"/> asks for a gap on behalf of one request; this one is the site asking, and it
    /// binds every later caller rather than the one that was told. A <c>429</c> answered to one worker is not
    /// a fact about that worker, and the other two hitting the same host would otherwise carry on into it.
    /// <para>
    /// Written down where the pacing already lives, so the wait costs no thread and no retry of its own: the
    /// next attempt enters the gate like any other and finds it closed. A longer hold already in place is kept
    /// — being asked twice never shortens the wait.
    /// </para>
    /// </remarks>
    void Rest(Uri url, TimeSpan gap);

    /// <summary>The soonest this host could let a caller in, as far as is known.</summary>
    /// <param name="url">The address in question; only its host matters.</param>
    /// <remarks>
    /// For choosing what to do next, never for deciding whether it may be done: <see cref="EnterAsync"/>
    /// remains the only thing that grants a turn, and an answer here can be out of date by the time it is read.
    /// Being approximate is what makes it useful — a caller about to wait at a shut gate can spend the same
    /// moment on a site that is open, and where the guess was wrong it waits at the gate exactly as it would
    /// have anyway.
    /// <para>
    /// An instant rather than a yes or no, and that distinction was measured rather than reasoned. A yes-or-no
    /// answer calls a host busy for the twenty milliseconds of its own gap and a host resting for five seconds
    /// by the same word, so a chooser with nothing open in front of it picks between them at random — and picks
    /// the five seconds a third of the time. What is being ranked is how long the wait would be.
    /// </para>
    /// <para>
    /// Without it a run can stop while there is nothing wrong with it: workers handed addresses regardless of
    /// whose gate is shut all sit out a refusal together while a hundred other sites go unread.
    /// </para>
    /// </remarks>
    DateTimeOffset OpensAt(Uri url);
}
