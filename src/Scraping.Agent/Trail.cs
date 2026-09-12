using Microsoft.Playwright;

namespace Snail.Toolkit.Web.Scraping.Agent;

/// <summary>
/// Keeps the last few addresses a page asked the site for.
/// </summary>
/// <remarks>
/// This is the sharpest thing a fruitless step can say. Silence means the page never noticed the move at all,
/// which is the defect a walkthrough is written to catch; a request to somewhere else means the move landed,
/// but not where the errand meant it to. Bounded because a page with analytics and web fonts asks for dozens
/// of things and the list is read by a person.
/// </remarks>
internal sealed class Trail : IDisposable
{
    private const int Kept = 8;

    private readonly IPage _page;
    private readonly Queue<string> _asked = new();
    private readonly Lock _gate = new();

    private Trail(IPage page)
    {
        _page = page;
        _page.Request += Note;
    }

    /// <summary>The addresses seen while this trail was being followed, oldest first.</summary>
    public IReadOnlyList<string> Asked
    {
        get
        {
            lock (_gate)
                return [.. _asked];
        }
    }

    /// <summary>Starts following what a page asks for.</summary>
    public static Trail Follows(IPage page) => new(page);

    /// <inheritdoc />
    public void Dispose() => _page.Request -= Note;

    private void Note(object? sender, IRequest request)
    {
        lock (_gate)
        {
            _asked.Enqueue(request.Url);

            if (_asked.Count > Kept)
                _asked.Dequeue();
        }
    }
}
