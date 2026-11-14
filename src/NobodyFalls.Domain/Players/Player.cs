using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Domain.Players;

public enum PlayerKind
{
    Human,

    /// <summary>A Watcher: a non-player character, never presented as a real player.</summary>
    Watcher,
}

public enum PlayerStatus
{
    /// <summary>Signed up before the season opened; cannot give or receive hands yet.</summary>
    Waitlisted,

    /// <summary>Alive: in play and counted among the living.</summary>
    Active,

    /// <summary>Fell at a midnight and has not come back yet.</summary>
    Fallen,
}

/// <summary>What happened when a fallen player came back.</summary>
public enum ReturnOutcome
{
    /// <summary>The player was not fallen: nothing to do.</summary>
    NotFallen,

    /// <summary>Back in play for the current day.</summary>
    BackToday,

    /// <summary>Too close to midnight: back in play when the next day starts.</summary>
    BackTomorrow,
}

/// <summary>
/// The life cycle of a player. Day-to-day state (hands received, threshold) lives in
/// the night snapshot, not here: this entity only changes on sign-up, falls and returns.
/// </summary>
public sealed class Player
{
    private Player(PlayerId id, PlayerKind kind, Pseudo pseudo, PlayerStatus status, GameDay? joinedDay, PlayerId? invitedBy)
    {
        Id = id;
        Kind = kind;
        Pseudo = pseudo;
        Status = status;
        JoinedDay = joinedDay;
        InvitedBy = invitedBy;
    }

    public PlayerId Id { get; }

    public PlayerKind Kind { get; }

    public Pseudo Pseudo { get; private set; }

    public PlayerStatus Status { get; private set; }

    /// <summary>The day the player entered the game; null while on the waiting list.</summary>
    public GameDay? JoinedDay { get; private set; }

    /// <summary>Who invited this player (the invitation tree).</summary>
    public PlayerId? InvitedBy { get; }

    public int FallsCount { get; private set; }

    public GameDay? LastFallDay { get; private set; }

    /// <summary>Set when a fallen player came back too close to midnight.</summary>
    public GameDay? ReturnsOnDay { get; private set; }

    public bool IsAlive => Status == PlayerStatus.Active;

    /// <summary>
    /// Creates a human player. Outside an open season the player waits for the opening
    /// night; inside one, they are in play immediately (and immune at the first midnight).
    /// </summary>
    public static Player Register(PlayerId id, Pseudo pseudo, GameDay today, bool seasonOpen, PlayerId? invitedBy)
    {
        ArgumentNullException.ThrowIfNull(pseudo);
        return seasonOpen
            ? new Player(id, PlayerKind.Human, pseudo, PlayerStatus.Active, today, invitedBy)
            : new Player(id, PlayerKind.Human, pseudo, PlayerStatus.Waitlisted, null, invitedBy);
    }

    /// <summary>Rebuilds a player loaded from storage. No rule is checked: the data was valid when saved.</summary>
    public static Player Restore(
        PlayerId id,
        PlayerKind kind,
        Pseudo pseudo,
        PlayerStatus status,
        GameDay? joinedDay,
        PlayerId? invitedBy,
        int fallsCount,
        GameDay? lastFallDay,
        GameDay? returnsOnDay) =>
        new(id, kind, pseudo, status, joinedDay, invitedBy)
        {
            FallsCount = fallsCount,
            LastFallDay = lastFallDay,
            ReturnsOnDay = returnsOnDay,
        };

    public static Player CreateWatcher(PlayerId id, Pseudo pseudo)
    {
        ArgumentNullException.ThrowIfNull(pseudo);
        return new Player(id, PlayerKind.Watcher, pseudo, PlayerStatus.Active, null, null);
    }

    /// <summary>The season opens for everyone at once: waitlisted players enter the game.</summary>
    public void EnterOpeningSeason(GameDay openingDay)
    {
        if (Status == PlayerStatus.Waitlisted)
        {
            Status = PlayerStatus.Active;
            JoinedDay = openingDay;
        }
    }

    /// <summary>The player did not get enough hands at the midnight that ends <paramref name="day"/>.</summary>
    public void Fall(GameDay day)
    {
        if (Kind == PlayerKind.Watcher)
        {
            throw new InvalidOperationException("Watchers never fall.");
        }

        if (Status != PlayerStatus.Active)
        {
            throw new InvalidOperationException($"Only a living player can fall (status: {Status}).");
        }

        Status = PlayerStatus.Fallen;
        FallsCount++;
        LastFallDay = day;
        ReturnsOnDay = null;
    }

    /// <summary>
    /// A fallen player comes back automatically on their next visit, with nothing lost.
    /// Idempotent: calling it again changes nothing.
    /// </summary>
    public ReturnOutcome Return(DateTimeOffset visitAt, GameRules rules)
    {
        if (Status != PlayerStatus.Fallen)
        {
            return ReturnOutcome.NotFallen;
        }

        var returnDay = ReturnPolicy.ReturnDay(visitAt, rules);
        if (returnDay == GameDay.Of(visitAt))
        {
            Status = PlayerStatus.Active;
            ReturnsOnDay = null;
            return ReturnOutcome.BackToday;
        }

        ReturnsOnDay = returnDay;
        return ReturnOutcome.BackTomorrow;
    }

    /// <summary>Applied when <paramref name="day"/> starts for a player who returned late the day before.</summary>
    public void CompleteScheduledReturn(GameDay day)
    {
        if (Status == PlayerStatus.Fallen && ReturnsOnDay is { } scheduled && scheduled <= day)
        {
            Status = PlayerStatus.Active;
            ReturnsOnDay = null;
        }
    }

    public void Rename(Pseudo pseudo)
    {
        ArgumentNullException.ThrowIfNull(pseudo);
        Pseudo = pseudo;
    }
}
