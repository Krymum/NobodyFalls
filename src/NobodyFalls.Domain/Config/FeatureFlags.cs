using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Config;

/// <summary>
/// A switch the team can flip without deploying (ADR 0022). Every key is declared here
/// with its default; the database only stores the overrides, so an unknown key cannot
/// exist and removing a flag from the code removes it everywhere.
/// </summary>
public sealed record FeatureFlag(string Key, bool Default, string Description);

public static class FeatureFlags
{
    /// <summary>Proposing ideas and voting. Reading them stays open.</summary>
    public static readonly FeatureFlag IdeasOpen = new("ideas", true, "Players can propose ideas and vote.");

    /// <summary>Reporting a player or an idea.</summary>
    public static readonly FeatureFlag ReportsOpen = new("reports", true, "Players can report a player or an idea.");

    /// <summary>
    /// Support requests. Contesting a sanction and asking for one's data to be erased are
    /// rights, not features: they stay open whatever this flag says.
    /// </summary>
    public static readonly FeatureFlag ContactOpen = new("contact", true, "Players can write to support.");

    public static IReadOnlyList<FeatureFlag> All { get; } = [IdeasOpen, ReportsOpen, ContactOpen];

    public static FeatureFlag? Find(string? key) => All.FirstOrDefault(f => f.Key == key);
}

public static class FeatureErrors
{
    public static readonly DomainError Disabled = new("feature.disabled", "This feature is switched off for now.");
    public static readonly DomainError UnknownFlag = new("flag.not_found", "No feature flag has this key.");
    public static readonly DomainError EnabledMissing = new("flag.enabled", "Say whether the flag is on or off.");
}
