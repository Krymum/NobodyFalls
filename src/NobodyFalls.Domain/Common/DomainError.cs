namespace NobodyFalls.Domain.Common;

/// <summary>
/// A rule violation. <see cref="Code"/> is stable and machine-readable: the API
/// returns it and the front end translates it, so it must never be reworded.
/// </summary>
public sealed record DomainError(string Code, string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}
