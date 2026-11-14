using System.Globalization;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Night;
using NobodyFalls.Domain.Players;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Testing;

/// <summary>Builds a day to resolve, in the vocabulary of the rules.</summary>
public sealed class DayBuilder
{
    public static readonly GameDay Day = GameDay.Parse("2026-11-13");

    private readonly List<PlayerDayEntry> _players = [];
    private readonly List<HandRecord> _hands = [];
    private long _nextHandId = 1;

    public GameRules Rules { get; init; } = GameRules.Default;

    public int PreviousStreak { get; init; }

    public long PreviousDeathsAllTime { get; init; }

    public static DateTimeOffset At(string time) =>
        Day.StartsAt + TimeSpan.Parse(time, CultureInfo.InvariantCulture);

    public PlayerId Player(string name, int required = 2, bool immune = false)
    {
        var id = Ids.Of(name);
        _players.Add(new PlayerDayEntry(id, required, immune));
        return id;
    }

    public DayBuilder Hand(PlayerId giver, PlayerId receiver, string at = "12:00", bool friend = false)
    {
        _hands.Add(new HandRecord(_nextHandId++, giver, PlayerKind.Human, receiver, At(at), friend));
        return this;
    }

    public DayBuilder WatcherHand(PlayerId watcher, PlayerId receiver, string at = "22:00")
    {
        _hands.Add(new HandRecord(_nextHandId++, watcher, PlayerKind.Watcher, receiver, At(at), false));
        return this;
    }

    public DaySnapshot Build() => new(Day, Rules, _players, _hands, PreviousStreak, PreviousDeathsAllTime);

    public NightOutcome Resolve() => NightResolver.Resolve(Build());
}
