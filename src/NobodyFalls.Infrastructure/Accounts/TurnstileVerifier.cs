using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NobodyFalls.Application.Accounts;

namespace NobodyFalls.Infrastructure.Accounts;

public sealed class TurnstileOptions
{
    public const string Section = "Turnstile";

    /// <summary>Public key, sent to the browser widget.</summary>
    public string SiteKey { get; set; } = "";

    /// <summary>
    /// Secret key. Cloudflare publishes test keys that always pass
    /// (<c>1x0000000000000000000000000000000AA</c>), used in development.
    /// </summary>
    public string SecretKey { get; set; } = "";
}

/// <summary>Verifies Turnstile tokens with Cloudflare. Fails closed: no answer means no.</summary>
internal sealed partial class TurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options, ILogger<TurnstileVerifier> logger) : IHumanVerifier
{
    public static readonly Uri Endpoint = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(options.Value.SecretKey))
        {
            return false;
        }

        var form = new Dictionary<string, string> { ["secret"] = options.Value.SecretKey, ["response"] = token };
        if (!string.IsNullOrEmpty(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await http.PostAsync(Endpoint, content, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);
            return result?.Success == true;
        }
        catch (HttpRequestException e)
        {
            LogUnavailable(e);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile could not be reached; the request is refused.")]
    private partial void LogUnavailable(Exception exception);

    private sealed record SiteVerifyResponse([property: JsonPropertyName("success")] bool Success);
}
