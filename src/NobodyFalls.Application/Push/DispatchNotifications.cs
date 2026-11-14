using NobodyFalls.Application.Abstractions;

namespace NobodyFalls.Application.Push;

public sealed record DispatchRun(int Notifications, int Delivered, int Gone, int Skipped);

/// <summary>
/// Sends what the outbox holds: respects preferences, writes in the player's language,
/// forgets subscriptions the browser revoked. A notification is marked sent once its
/// devices were tried; if none could be reached (push service down) it is retried after
/// its lease. A crash only means a retry, never a loss.
/// </summary>
public sealed class DispatchNotifications(
    INotificationOutbox outbox,
    IPushStore store,
    IPushSender sender,
    INotificationDirectory directory,
    INotificationTexts texts,
    IClock clock)
{
    public const int BatchSize = 100;

    public async Task<DispatchRun> RunOnceAsync(CancellationToken cancellationToken)
    {
        var pending = await outbox.ClaimAsync(BatchSize, clock.UtcNow, cancellationToken);
        int delivered = 0, gone = 0, skipped = 0;

        foreach (var notification in pending)
        {
            var preferences = await store.PreferencesAsync(notification.Recipient, cancellationToken);
            var subscriptions = preferences.Allows(notification.Kind)
                ? await store.SubscriptionsAsync(notification.Recipient, cancellationToken)
                : [];

            var failed = 0;
            if (subscriptions.Count == 0)
            {
                skipped++;
            }
            else
            {
                var locale = await directory.LocaleOfAsync(notification.Recipient, cancellationToken);
                var names = await directory.PseudosAsync(notification.Players, cancellationToken);
                var message = texts.Render(notification.Kind, locale, names);

                foreach (var subscription in subscriptions)
                {
                    switch (await sender.SendAsync(subscription, message, cancellationToken))
                    {
                        case PushDelivery.Delivered:
                            delivered++;
                            break;
                        case PushDelivery.Gone:
                            gone++;
                            await store.ForgetAsync(subscription.Endpoint, cancellationToken);
                            break;
                        case PushDelivery.Failed:
                        default:
                            failed++;
                            break;
                    }
                }
            }

            if (failed == 0 || failed < subscriptions.Count)
            {
                await outbox.MarkSentAsync(notification.Id, clock.UtcNow, cancellationToken);
            }
        }

        return new DispatchRun(pending.Count, delivered, gone, skipped);
    }
}
