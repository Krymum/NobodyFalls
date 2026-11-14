namespace NobodyFalls.Application.Abstractions;

/// <summary>
/// The only source of "now" for game logic. Never use <see cref="DateTimeOffset.UtcNow"/>
/// or SQL <c>now()</c> for rules: in development the clock can be moved forward to
/// reach midnight in seconds (ADR 0006).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Development-only control over the simulated clock.</summary>
public interface IClockControl
{
    /// <summary>Current offset between the simulated and the real time.</summary>
    TimeSpan Offset { get; }

    Task SetOffsetAsync(TimeSpan offset, CancellationToken cancellationToken);
}
