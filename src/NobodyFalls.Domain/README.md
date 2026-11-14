# NobodyFalls.Domain

The rules of the game, in plain C#. No NuGet dependency, no I/O, no clock:
time is always passed in. See [ADR 0002](../../docs/adr/0002-layered-dotnet-solution.md).

| Rule (`docs/GAME_DESIGN.md`) | Code |
|---|---|
| Game day, midnight UTC (§2) | `Common/GameDay` |
| Three hands a day, who may hold whom (§2) | `Rules/HandPolicy` |
| Threshold, low population, fragile newcomers, storms (§2, §3) | `Rules/ThresholdPolicy`, `Rules/StormPolicy` |
| Falling and coming back (§2) | `Players/Player`, `Rules/ReturnPolicy` |
| Midnight: falls, Guardian points, last stand, life debts, streak, deaths (§2) | `Night/NightResolver` |
| Edge of the void ordering (§2) | `Rules/EdgeOrdering` |
| Watchers (§3) | `Rules/WatcherPolicy` |
| Seasons and waiting list (§3) | `Seasons/Season` |
| Friends, limit, invitations (§4) | `Friends/FriendPolicy`, `Friends/PlayerPair` |
| Characters as data, skins, unlocks (§5) | `Skins/*` |
| Every tunable number | `Rules/GameRules` |

`NightResolver` is the executable specification of midnight. Production runs the
same rules as set-based SQL, checked against it by differential tests
([ADR 0003](../../docs/adr/0003-midnight-resolution-reference-and-sql.md)).

Error codes (`hand.none_left`, `friend.limit_reached`…) are part of the API
contract: the front end translates them, so never rename one.
