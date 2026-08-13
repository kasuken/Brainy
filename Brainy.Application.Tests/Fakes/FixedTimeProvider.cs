namespace Brainy.Application.Tests.Fakes;

/// <summary>
/// A <see cref="TimeProvider"/> frozen at a fixed instant so due-date logic can be
/// tested deterministically (no midnight or time-zone races).
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan elapsed) => _now = _now.Add(elapsed);
}
