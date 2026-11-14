using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NobodyFalls.Application.Push;
using AppPushMessage = NobodyFalls.Application.Push.PushMessage;
using AppPushSubscription = NobodyFalls.Application.Push.PushSubscription;

namespace NobodyFalls.Infrastructure.Push;

/// <summary>Web Push with VAPID (RFC 8030/8292). The payload is JSON read by the service worker.</summary>
internal sealed partial class WebPushSender(
    HttpClient http,
    VapidKeyStore keys,
    IOptions<PushOptions> options,
    ILogger<WebPushSender> logger) : IPushSender
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PushDelivery> SendAsync(AppPushSubscription subscription, AppPushMessage message, CancellationToken cancellationToken)
    {
        var vapid = await keys.GetAsync(cancellationToken);
        var client = new PushServiceClient(http)
        {
            DefaultAuthentication = new VapidAuthentication(vapid.PublicKey, vapid.PrivateKey) { Subject = options.Value.Subject },
        };

        var target = new Lib.Net.Http.WebPush.PushSubscription
        {
            Endpoint = subscription.Endpoint,
            Keys = new Dictionary<string, string> { ["p256dh"] = subscription.P256dh, ["auth"] = subscription.Auth },
        };

        // Topic: a newer message with the same topic replaces an undelivered older one.
        var push = new Lib.Net.Http.WebPush.PushMessage(JsonSerializer.Serialize(message, Json))
        {
            Topic = Topic(message.Tag),
            TimeToLive = (int)TimeSpan.FromHours(6).TotalSeconds,
            Urgency = PushMessageUrgency.Normal,
        };

        try
        {
            await client.RequestPushMessageDeliveryAsync(target, push, cancellationToken);
            return PushDelivery.Delivered;
        }
        catch (PushServiceClientException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushDelivery.Gone;
        }
        catch (Exception e) when (e is PushServiceClientException or HttpRequestException or TaskCanceledException)
        {
            LogFailed(e);
            return PushDelivery.Failed;
        }
    }

    /// <summary>Topics are at most 32 URL-safe characters.</summary>
    private static string Topic(string tag)
    {
        var safe = new string(tag.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        return safe.Length <= 32 ? safe : safe[..32];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A push message could not be delivered; it will be retried.")]
    private partial void LogFailed(Exception exception);
}
