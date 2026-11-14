using System.Net.Http.Json;
using NobodyFalls.Domain.Security;
using NobodyFalls.Domain.Staff;
using NobodyFalls.Testing;

namespace NobodyFalls.Api.Tests.Support;

/// <summary>A team member's browser, and the authenticator in their pocket.</summary>
public sealed class StaffMember(HttpClient client, Guid id, ManualTimeProvider time)
{
    private long _lastStep = -1;

    public HttpClient Client { get; } = client;

    public Guid Id { get; } = id;

    public byte[]? Secret { get; set; }

    /// <summary>The next code the authenticator would show (never the same step twice).</summary>
    public string NextCode()
    {
        var step = Math.Max(Totp.StepAt(time.GetUtcNow()), _lastStep + 1);
        _lastStep = step;
        return Totp.Code(Secret ?? throw new InvalidOperationException("Not enrolled."), step);
    }
}

public sealed record EnrollBody(string Secret, string Uri);

public sealed record RecoveryCodesBody(IReadOnlyList<string> RecoveryCodes);

public static class StaffSupport
{
    public const string Admin = "/api/v1/admin";

    /// <summary>
    /// Signs up a player, gives them roles, and (by default) sets up their authenticator,
    /// which also opens their admin session.
    /// </summary>
    public static async Task<StaffMember> StaffAsync(this ApiApp app, string pseudo, StaffRole[] roles, bool enroll = true)
    {
        ArgumentNullException.ThrowIfNull(app);
        var (client, id) = await app.SignUpAsync(pseudo);
        await app.Seed.ExecAsync(
            "insert into staff_members (player_id, roles, granted_at) values (@id, @roles, now())",
            ("id", id),
            ("roles", roles.Select(r => r.Name()).ToArray()));
        var member = new StaffMember(client, id, app.Time);
        if (enroll)
        {
            await member.EnrollAsync();
        }

        return member;
    }

    public static async Task<IReadOnlyList<string>> EnrollAsync(this StaffMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var enroll = await member.Client.PostAsync(new Uri($"{Admin}/session/totp/enroll", UriKind.Relative), null);
        enroll.EnsureSuccessStatusCode();
        member.Secret = Base32.Decode((await enroll.Content.ReadFromJsonAsync<EnrollBody>())!.Secret);

        var confirm = await member.Client.PostAsJsonAsync($"{Admin}/session/totp/confirm", new { code = member.NextCode() });
        confirm.EnsureSuccessStatusCode();
        return (await confirm.Content.ReadFromJsonAsync<RecoveryCodesBody>())!.RecoveryCodes;
    }

    public static Task<HttpResponseMessage> LoginAsync(this StaffMember member, string? code = null) =>
        member.Client.PostAsJsonAsync($"{Admin}/session/login", new { code = code ?? member.NextCode() });

    public static Task<HttpResponseMessage> ReauthAsync(this StaffMember member) =>
        member.Client.PostAsJsonAsync($"{Admin}/session/reauth", new { code = member.NextCode() });
}
