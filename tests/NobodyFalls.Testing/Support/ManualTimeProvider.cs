namespace NobodyFalls.Testing;

/// <summary>
/// Real time under the test's control: sign-in codes, session lifetimes and locks follow
/// it (the game clock is a separate <c>IClock</c>).
/// </summary>
public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset now) => _now = now;
}
