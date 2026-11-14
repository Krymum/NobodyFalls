using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Players;
using NobodyFalls.Domain.Skins;

namespace NobodyFalls.Api.Endpoints;

// Request and response bodies that are specific to the HTTP API. Read models of the
// game come straight from NobodyFalls.Application.Queries.

public enum SessionState
{
    Anonymous,

    /// <summary>Signed in with a provider, no character yet: show the onboarding.</summary>
    PendingRegistration,

    Player,
}

public sealed record SessionResponse(
    SessionState State,
    SessionPlayer? Player,
    ReturnOutcome? Returned,
    IReadOnlyList<string> Providers,
    string TurnstileSiteKey,
    bool DevTools);

public sealed record RegistrationRequest(string? Pseudo, Appearance Appearance, string? Locale, string? InviteCode, string? TurnstileToken);

public sealed record GiveHandRequest(Guid ReceiverId, string? TurnstileToken);

public sealed record FriendRequestBody(string? Pseudo);

public sealed record FriendRequestResponse(string Outcome);

public sealed record SettingsRequest(string? Locale, string? MapNameVisibility);

public sealed record SettingsChoices(IReadOnlyList<string> Locales, IReadOnlyList<string> MapNameVisibility)
{
    public static SettingsChoices Current { get; } =
        new(Application.Accounts.Locales.Supported, Application.Players.MapNameVisibility.Values);
}

public sealed record IdResponse(Guid Id);

public sealed record ListResponse<T>(IReadOnlyList<T> Items);
