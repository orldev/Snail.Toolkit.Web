namespace Snail.Toolkit.Web.Search.Tests;

/// <summary>A clock the test moves by hand, so a rest can run out without anybody waiting for it.</summary>
internal sealed class Clock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
