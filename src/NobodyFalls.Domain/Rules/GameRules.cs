using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Rules;

/// <summary>
/// Every tunable number of the game. Stored in the database and editable without
/// redeploying; the values in effect for a game day are frozen when the day starts
/// (at the previous midnight) so that the rules never change under a player's feet.
/// </summary>
/// <remarks>Defaults are the values of <c>docs/GAME_DESIGN.md</c>.</remarks>
public sealed record GameRules
{
    public static GameRules Default { get; } = new();

    /// <summary>Hands each player can give per day. They do not carry over.</summary>
    public int HandsPerDay { get; init; } = 3;

    /// <summary>Hands needed to survive once the population is large enough.</summary>
    public int BaseRequired { get; init; } = 2;

    /// <summary>Hands needed while the living population is below <see cref="LowPopulationBelow"/>.</summary>
    public int LowPopulationRequired { get; init; } = 1;

    public int LowPopulationBelow { get; init; } = 50;

    /// <summary>Extra hands a new player needs on their second night (the first one is immune).</summary>
    public int FragileExtra { get; init; } = 1;

    /// <summary>Extra hands everyone needs on a storm night.</summary>
    public int StormExtra { get; init; } = 1;

    /// <summary>How long before midnight a storm is announced; it cannot be scheduled later than that.</summary>
    public TimeSpan StormNotice { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// A fallen player who comes back with less than this before midnight is put back
    /// in play the next day, so they do not fall again minutes after returning.
    /// </summary>
    public TimeSpan ReturnCutoff { get; init; } = TimeSpan.FromHours(6);

    /// <summary>The "last stand" window before midnight.</summary>
    public TimeSpan LastRampartWindow { get; init; } = TimeSpan.FromHours(1);

    public int PointsFriend { get; init; } = 1;

    public int PointsStranger { get; init; } = 2;

    public int LastRampartBonus { get; init; } = 2;

    public WatcherRules Watchers { get; init; } = new();

    public int FriendLimit { get; init; } = 500;

    /// <summary>Nights held together (either direction) before two players are suggested as friends.</summary>
    public int SuggestionMinNights { get; init; } = 2;

    /// <summary>UTC time of the daily "friends still below the threshold" push.</summary>
    public TimeOnly FriendReminderAt { get; init; } = new(21, 0);

    /// <summary>Returns every inconsistency; an empty list means the rules can be applied.</summary>
    public IReadOnlyList<DomainError> Validate()
    {
        var errors = new List<DomainError>();
        void Require(bool condition, string field, string message)
        {
            if (!condition)
            {
                errors.Add(new DomainError($"rules.{field}", message));
            }
        }

        Require(HandsPerDay is >= 1 and <= 10, "hands_per_day", "Between 1 and 10 hands per day.");
        Require(BaseRequired >= 1, "base_required", "At least one hand is required.");
        Require(LowPopulationRequired >= 0, "low_population_required", "Cannot be negative.");
        Require(LowPopulationBelow >= 0, "low_population_below", "Cannot be negative.");
        Require(FragileExtra >= 0, "fragile_extra", "Cannot be negative.");
        Require(StormExtra >= 0, "storm_extra", "Cannot be negative.");
        Require(StormNotice >= TimeSpan.Zero && StormNotice < TimeSpan.FromDays(1), "storm_notice", "Between 0 and 24 hours.");
        Require(ReturnCutoff >= TimeSpan.Zero && ReturnCutoff < TimeSpan.FromDays(1), "return_cutoff", "Between 0 and 24 hours.");
        Require(LastRampartWindow >= TimeSpan.Zero && LastRampartWindow < TimeSpan.FromDays(1), "last_rampart_window", "Between 0 and 24 hours.");
        Require(PointsFriend >= 0 && PointsStranger >= 0 && LastRampartBonus >= 0, "points", "Points cannot be negative.");
        Require(FriendLimit >= 1, "friend_limit", "At least one friend must be allowed.");
        Require(SuggestionMinNights >= 1, "suggestion_min_nights", "At least one night.");
        errors.AddRange(Watchers.Validate());
        return errors;
    }
}

/// <summary>
/// Watchers are non-player characters, always labelled as such, who hold players
/// nobody holds while the population is small. They fade out as it grows.
/// </summary>
public sealed record WatcherRules
{
    /// <summary>Number of Watchers when the population is (almost) empty.</summary>
    public int MaxCount { get; init; } = 10;

    /// <summary>Living population at which no Watcher remains.</summary>
    public int DisappearAtPopulation { get; init; } = 200;

    /// <summary>UTC time of the daily Watcher pass.</summary>
    public TimeOnly PassAt { get; init; } = new(22, 0);

    public int HandsPerWatcher { get; init; } = 3;

    public IReadOnlyList<DomainError> Validate()
    {
        var errors = new List<DomainError>();
        if (MaxCount < 0)
        {
            errors.Add(new DomainError("rules.watchers.max_count", "Cannot be negative."));
        }

        if (DisappearAtPopulation < 0)
        {
            errors.Add(new DomainError("rules.watchers.disappear_at_population", "Cannot be negative."));
        }

        if (HandsPerWatcher < 0)
        {
            errors.Add(new DomainError("rules.watchers.hands_per_watcher", "Cannot be negative."));
        }

        return errors;
    }
}
