using NobodyFalls.Domain.Common;

namespace NobodyFalls.Application.Push;

/// <summary>The kinds of push notifications; the values are stored in the outbox.</summary>
public static class NotificationKinds
{
    /// <summary>You fell at midnight. (Surviving is the normal case: no push for it.)</summary>
    public const string Fell = "night.fell";

    public const string FriendsFell = "friends.fell";
    public const string SavedBy = "life.saved";
    public const string SkinsUnlocked = "skins.unlocked";
    public const string Storm = "storm.announced";

    /// <summary>One reminder a day, three hours before midnight, about friends still below the threshold.</summary>
    public const string FriendsBelow = "friends.below";
}

/// <summary>What a player agreed to receive. Everything is on by default.</summary>
public sealed record NotificationPreferences(bool FriendBelow, bool FriendFell, bool SavedMe, bool Fell, bool Storm)
{
    public static NotificationPreferences Default { get; } = new(true, true, true, true, true);

    public bool Allows(string kind) => kind switch
    {
        NotificationKinds.FriendsBelow => FriendBelow,
        NotificationKinds.FriendsFell => FriendFell,
        NotificationKinds.SavedBy => SavedMe,
        NotificationKinds.Fell => Fell,
        NotificationKinds.Storm => Storm,
        _ => true,
    };
}

public sealed record PushSubscription(string Endpoint, string P256dh, string Auth);

/// <summary>A notification waiting in the outbox, written by midnight or a scheduled job.</summary>
/// <param name="Players">Players mentioned in the message (friends who fell, savers…).</param>
public sealed record PendingNotification(long Id, PlayerId Recipient, string Kind, GameDay Day, IReadOnlyList<PlayerId> Players);

/// <summary>A message ready for the browser's service worker.</summary>
/// <param name="Tag">Same tag = the new notification replaces the old one on the device.</param>
public sealed record PushMessage(string Title, string Body, string Url, string Tag);

public enum PushDelivery
{
    Delivered,

    /// <summary>The browser unsubscribed: the subscription must be forgotten.</summary>
    Gone,

    Failed,
}

/// <summary>Sends a Web Push message (VAPID).</summary>
public interface IPushSender
{
    Task<PushDelivery> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken cancellationToken);
}

public interface IPushStore
{
    Task SubscribeAsync(PlayerId player, PushSubscription subscription, string? userAgent, DateTimeOffset now, CancellationToken cancellationToken);

    Task UnsubscribeAsync(PlayerId player, string endpoint, CancellationToken cancellationToken);

    Task<IReadOnlyList<PushSubscription>> SubscriptionsAsync(PlayerId player, CancellationToken cancellationToken);

    Task ForgetAsync(string endpoint, CancellationToken cancellationToken);

    Task<NotificationPreferences> PreferencesAsync(PlayerId player, CancellationToken cancellationToken);

    Task SavePreferencesAsync(PlayerId player, NotificationPreferences preferences, CancellationToken cancellationToken);
}

/// <summary>
/// The transactional outbox (ADR 0009). Notifications are claimed with a lease, so
/// several instances can send in parallel without sending anything twice.
/// </summary>
public interface INotificationOutbox
{
    Task<IReadOnlyList<PendingNotification>> ClaimAsync(int count, DateTimeOffset now, CancellationToken cancellationToken);

    Task MarkSentAsync(long id, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Information the texts need about the recipient and the people mentioned.</summary>
public interface INotificationDirectory
{
    Task<string> LocaleOfAsync(PlayerId player, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> PseudosAsync(IReadOnlyList<PlayerId> players, CancellationToken cancellationToken);
}

/// <summary>Localized notification texts (resources, FR and EN).</summary>
public interface INotificationTexts
{
    PushMessage Render(string kind, string locale, IReadOnlyList<string> names);
}

/// <summary>Writes the scheduled notifications of a day (storm announcement, friends reminder). Idempotent.</summary>
public interface INotificationScheduler
{
    Task<int> RunAsync(GameDay day, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>The VAPID public key browsers need to subscribe.</summary>
public interface IPushKeys
{
    Task<string> PublicKeyAsync(CancellationToken cancellationToken);
}
